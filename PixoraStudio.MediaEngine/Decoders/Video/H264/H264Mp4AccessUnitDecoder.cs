using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes one AVC picture carried in an MP4 sample using the managed I/P/B
/// slice decoders, frame DPB and MP4-aware POC output scheduler. Multiple
/// frame-coded slices are supported when they share picture identity and
/// reference-list ordering. In-band parameter-set changes are rejected.
/// </summary>
public sealed class H264Mp4AccessUnitDecoder
{
    private readonly H264AvcParameterSetCollection _parameterSets;
    private readonly H264DecodedPictureBuffer _decodedPictureBuffer = new();
    private readonly H264Mp4VideoOutputScheduler _outputScheduler;
    private readonly int? _maxNumReorderFramesFallback;
    private readonly Func<H264BitReader, int, bool, int, IH264CabacSliceDataReader> _cabacReaderFactory;
    private uint? _trackId;
    private uint? _timescale;
    private uint? _lastSampleNumber;
    private ulong? _lastDecodeTimeTicks;
    private bool _requiresReset;

    public H264Mp4AccessUnitDecoder(
        H264AvcParameterSetCollection parameterSets,
        int? maxNumReorderFramesFallback = null,
        H264CabacPerformanceProfiler? performanceProfiler = null)
        : this(
            parameterSets,
            maxNumReorderFramesFallback,
            (reader, qpY, isIntra, cabacInitIdc) =>
                new H264CabacSliceDataReader(
                    reader, qpY, isIntra, cabacInitIdc,
                    performanceProfiler: performanceProfiler))
    {
    }

    internal H264Mp4AccessUnitDecoder(
        H264AvcParameterSetCollection parameterSets,
        int? maxNumReorderFramesFallback,
        Func<H264BitReader, int, bool, int, IH264CabacSliceDataReader> cabacReaderFactory,
        H264Mp4VideoOutputScheduler? outputScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(parameterSets);
        ArgumentNullException.ThrowIfNull(cabacReaderFactory);
        if (parameterSets.SequenceParameterSets.Count == 0 ||
            parameterSets.PictureParameterSets.Count == 0)
            throw new NotSupportedException(
                "The MP4 access-unit decoder requires SPS and PPS entries in avcC; in-band parameter-set updates are not supported.");
        if (maxNumReorderFramesFallback is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxNumReorderFramesFallback));
        _parameterSets = parameterSets;
        _maxNumReorderFramesFallback = maxNumReorderFramesFallback;
        _cabacReaderFactory = cabacReaderFactory;
        _outputScheduler = outputScheduler ?? new H264Mp4VideoOutputScheduler();
    }

    /// <summary>
    /// Creates a track decoder by loading the active SPS/PPS collection from
    /// the first sample's avcC configuration.
    /// </summary>
    public static H264Mp4AccessUnitDecoder CreateFromSample(
        H264Mp4VideoSample firstSample,
        int? maxNumReorderFramesFallback = null,
        H264CabacPerformanceProfiler? performanceProfiler = null)
    {
        ArgumentNullException.ThrowIfNull(firstSample);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(firstSample.Configuration);
        return new H264Mp4AccessUnitDecoder(
            parameterSets, maxNumReorderFramesFallback, performanceProfiler);
    }

    public H264DecodedPictureBuffer DecodedPictureBuffer => _decodedPictureBuffer;
    public int PendingOutputCount => _outputScheduler.PendingCount;

    /// <summary>
    /// Decodes one MP4 sample. Samples with no VCL NAL unit (for example,
    /// SEI/AUD-only samples) return null. All VCL slices in a sample must
    /// describe the same frame-coded picture and use consistent reference lists.
    /// </summary>
    public H264Mp4AccessUnitDecodeResult? Decode(
        H264Mp4VideoSample sample,
        int firstSliceId = 0)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (_requiresReset)
            throw new InvalidOperationException(
                "The decoder encountered a failed picture decode. Call Reset before decoding another sample.");
        if (firstSliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(firstSliceId));
        if (sample.TrackId == 0 || sample.SampleNumber == 0)
            throw new FormatException("MP4 track and sample identifiers must be non-zero.");
        if (sample.Timescale == 0)
            throw new FormatException("The MP4 AVC sample has an invalid timescale.");
        if (_trackId is uint activeTrackId && activeTrackId != sample.TrackId)
            throw new InvalidOperationException("One decoder instance cannot switch MP4 tracks.");
        if (_timescale is uint activeTimescale && activeTimescale != sample.Timescale)
            throw new FormatException("The MP4 track timescale changed during decoding.");
        if (_lastSampleNumber is uint previousSample &&
            (previousSample == uint.MaxValue || sample.SampleNumber != previousSample + 1))
            throw new FormatException(
                "MP4 AVC samples must be supplied contiguously in decode order; reset before seeking.");
        if (_lastDecodeTimeTicks is ulong previousDecodeTime &&
            sample.DecodeTimeTicks < previousDecodeTime)
            throw new FormatException("MP4 AVC sample decode timestamps must be non-decreasing.");

        ValidateConfigurationIdentity(sample.Configuration);
        _trackId ??= sample.TrackId;
        _timescale ??= sample.Timescale;
        _lastSampleNumber = sample.SampleNumber;
        _lastDecodeTimeTicks = sample.DecodeTimeTicks;
        _requiresReset = true;

        H264NalUnit[] sliceNals = GetSliceNals(sample.NalUnits);
        if (sliceNals.Length == 0)
        {
            _requiresReset = false;
            return null;
        }

        H264SliceHeaderReader sliceHeaderReader = new();
        List<(H264NalUnit NalUnit, H264SliceHeader Header)> slices = new(sliceNals.Length);
        H264SequenceParameterSet? sps = null;
        H264PictureParameterSetHeader? pps = null;

        foreach (H264NalUnit nalUnit in sliceNals)
        {
            uint ppsId = sliceHeaderReader.ReadLeadingIdentifiers(nalUnit).PictureParameterSetId;
            if (!_parameterSets.PictureParameterSets.TryGetValue(
                ppsId, out H264PictureParameterSetHeader? currentPps) || currentPps is null)
                throw new FormatException($"The AVC slice references missing PPS {ppsId}.");
            if (!_parameterSets.SequenceParameterSets.TryGetValue(
                currentPps.SequenceParameterSetId, out H264SequenceParameterSet? currentSps) || currentSps is null)
                throw new FormatException(
                    $"PPS {currentPps.Id} references missing SPS {currentPps.SequenceParameterSetId}.");

            if (sps is null)
            {
                sps = currentSps;
                pps = currentPps;
            }
            else if (currentSps.Id != sps.Id || currentPps.Id != pps!.Id)
            {
                throw new NotSupportedException(
                    "All slices in one MP4 sample must use the same SPS and PPS.");
            }

            H264SliceHeader header = sliceHeaderReader.Read(
                nalUnit, currentSps, currentPps);
            ValidateSliceHeader(nalUnit, header);
            if (slices.Count != 0)
                ValidateSamePicture(
                    slices[0].NalUnit, slices[0].Header, nalUnit, header);
            slices.Add((nalUnit, header));
        }

        H264SequenceParameterSet activeSps = sps!;
        H264PictureParameterSetHeader activePps = pps!;
        if (!activeSps.FrameMbsOnlyFlag || activeSps.MbAdaptiveFrameFieldFlag)
            throw new NotSupportedException(
                "The MP4 access-unit decoder supports frame-coded pictures only.");
        ulong pictureMacroblockCount =
            ((ulong)activeSps.PicWidthInMbsMinus1 + 1) *
            ((ulong)activeSps.PicHeightInMapUnitsMinus1 + 1);
        if (slices.Any(item => (ulong)item.Header.FirstMacroblockInSlice >= pictureMacroblockCount))
            throw new FormatException("A slice first_mb_in_slice address lies outside the picture.");
        H264NalUnit firstNal = slices[0].NalUnit;
        H264SliceHeader firstSlice = slices[0].Header;
        if (firstNal.NalRefIdc != 0 && activeSps.MaxNumRefFrames == 0)
            throw new FormatException("A reference slice cannot use an SPS with max_num_ref_frames equal to zero.");
        if (firstNal.NalRefIdc != 0 && activeSps.MaxNumRefFrames > 16)
            throw new NotSupportedException("The managed decoded-picture buffer supports at most 16 reference frames.");

        H264DecodedPictureSurface surface = new(activeSps);
        H264MotionVectorPictureState? motionStateL0 = null;
        H264MotionVectorPictureState? motionStateL1 = null;
        IReadOnlyList<H264ReferencePicture>? referenceList0 = null;
        IReadOnlyList<H264ReferencePicture>? referenceList1 = null;

        // POC is calculated once per picture, not once per slice. Any failure
        // after accepting this sample faults the decoder until Reset so later
        // samples cannot continue from an incomplete reference chain.
        try
        {
            H264PictureOrderCount poc = _outputScheduler.CalculatePictureOrderCount(
                activeSps, firstSlice, firstNal.NalRefIdc);

            List<string> sliceDecodeSummaries = [];

            if (firstSlice.SliceType == H264SliceType.P)
            {
                motionStateL0 = new H264MotionVectorPictureState(
                    surface.MacroblockWidth, surface.MacroblockHeight);
                motionStateL1 = new H264MotionVectorPictureState(
                    surface.MacroblockWidth, surface.MacroblockHeight);
            }
            else if (firstSlice.SliceType == H264SliceType.B)
            {
                motionStateL0 = new H264MotionVectorPictureState(
                    surface.MacroblockWidth, surface.MacroblockHeight);
                motionStateL1 = new H264MotionVectorPictureState(
                    surface.MacroblockWidth, surface.MacroblockHeight);
            }
            else if (firstSlice.SliceType != H264SliceType.I)
            {
                throw new NotSupportedException(
                    $"Slice type {firstSlice.SliceType} is not supported by the MP4 access-unit decoder.");
            }

            // Decode slices in bitstream order. Each slice gets fresh entropy and
            // neighbour state, while picture-wide motion grids retain metadata
            // tagged with its owning slice ID.
            for (int i = 0; i < slices.Count; i++)
            {
                (H264NalUnit nalUnit, H264SliceHeader header) = slices[i];
                int currentSliceId = checked(firstSliceId + i);

                switch (header.SliceType)
                {
                    case H264SliceType.I:
                    {
                        H264IntraSliceDecodeResult result;
                        if (activePps.EntropyCodingModeFlag)
                        {
                            H264CabacIntraSliceDataDecoder decoder =
                                new(surface, CreateCabacReader);
                            decoder.ResetPictureContexts();
                            result = decoder.Decode(
                                nalUnit, activeSps, activePps, currentSliceId);
                        }
                        else
                        {
                            H264IntraSliceDataDecoder decoder = new(surface);
                            result = decoder.Decode(
                                nalUnit, activeSps, activePps, currentSliceId);
                        }

                        sliceDecodeSummaries.Add(
                            $"slice[{i}] first_mb={header.FirstMacroblockInSlice} decoded={result.MacroblocksDecoded} bits={result.BitsConsumed}");
                        break;
                    }

                    case H264SliceType.P:
                    {
                        // Keep entropy and neighbour contexts isolated per slice,
                        // but share the picture motion grid with slice-ID availability.
                        H264PInterSliceDataDecoder decoder = new(
                            surface, motionStateL0!, new H264PInterMacroblockReconstructor(),
                            CreateCabacReader);
                        H264PInterSliceDecodeResult result = decoder.Decode(
                            nalUnit, activeSps, activePps,
                            _decodedPictureBuffer, currentSliceId);
                        string cabacTrace = result.CabacDiagnostics.Count == 0
                            ? string.Empty
                            : $" cabac_trace=[{string.Join(" | ", result.CabacDiagnostics.TakeLast(24))}]";
                        sliceDecodeSummaries.Add(
                            $"slice[{i}] first_mb={header.FirstMacroblockInSlice} decoded={result.MacroblocksDecoded} bits={result.BitsConsumed}{cabacTrace}");

                        IReadOnlyList<H264ReferencePicture> currentList0 = result.ReferenceList0 ??
                            throw new InvalidOperationException("The P-slice decoder did not return its reference list.");
                        if (referenceList0 is null)
                        {
                            referenceList0 = currentList0;
                            referenceList1 = [];
                        }
                        else if (!referenceList0.SequenceEqual(currentList0))
                        {
                            throw new NotSupportedException(
                                "P slices in one picture must use identical RefPicList0 ordering for temporal-direct metadata.");
                        }

                        break;
                    }

                    case H264SliceType.B:
                    {
                        // Each B slice receives fresh syntax/neighbour state;
                        // motion vectors are retained in the picture-wide slice-tagged grids.
                        H264BInterSliceDataDecoder decoder = new(
                            surface, motionStateL0!, motionStateL1!,
                            new H264BInterMacroblockReconstructor(), CreateCabacReader);
                        H264BInterSliceDecodeResult result = decoder.Decode(
                            nalUnit, activeSps, activePps, _decodedPictureBuffer,
                            poc.PictureOrderCount, currentSliceId);
                        sliceDecodeSummaries.Add(
                            $"slice[{i}] first_mb={header.FirstMacroblockInSlice} decoded={result.MacroblocksDecoded} bits={result.BitsConsumed}");

                        H264BReferencePictureLists currentLists = result.ReferencePictureLists ??
                            throw new InvalidOperationException("The B-slice decoder did not return its reference lists.");
                        if (referenceList0 is null)
                        {
                            referenceList0 = currentLists.List0;
                            referenceList1 = currentLists.List1;
                        }
                        else if (!referenceList0.SequenceEqual(currentLists.List0) ||
                                 !referenceList1!.SequenceEqual(currentLists.List1))
                        {
                            throw new NotSupportedException(
                                "B slices in one picture must use identical reference-list ordering for temporal-direct metadata.");
                        }

                        break;
                    }

                    default:
                        throw new NotSupportedException(
                            $"Slice type {header.SliceType} is not supported by the MP4 access-unit decoder.");
                }
            }

            if (!surface.IsComplete)
            {
                IReadOnlyList<uint> missingMacroblocks =
                    surface.GetUndecodedMacroblockAddresses();
                string missingPreview = string.Join(",",
                    missingMacroblocks.Take(32));
                string missingSuffix = missingMacroblocks.Count > 32
                    ? $",...(+{missingMacroblocks.Count - 32} more)"
                    : string.Empty;
                string sliceSummary = string.Join("; ", sliceDecodeSummaries);
                throw new NotSupportedException(
                    $"The MP4 sample {sample.SampleNumber} reconstructed " +
                    $"{pictureMacroblockCount - (ulong)missingMacroblocks.Count}/" +
                    $"{pictureMacroblockCount} macroblocks. Missing raster addresses: " +
                    $"[{missingPreview}{missingSuffix}]. Slice results: {sliceSummary}.");
            }

            if (firstNal.NalRefIdc != 0)
            {
                int maxFrameNum = 1 << checked((int)activeSps.Log2MaxFrameNumMinus4 + 4);
                _decodedPictureBuffer.StoreDecodedPicture(
                    surface,
                    new H264ReferencePicture(checked((int)firstSlice.FrameNum), poc.PictureOrderCount),
                    isReference: true,
                    maxNumRefFrames: activeSps.MaxNumRefFrames,
                    currentFrameNum: checked((int)firstSlice.FrameNum),
                    maxFrameNum: maxFrameNum,
                    marking: firstSlice.DecodedReferenceMarking,
                    idrPicture: firstNal.NalUnitType == 5,
                    motionVectorStateL0: motionStateL0,
                    motionVectorStateL1: motionStateL1,
                    referenceList0: referenceList0,
                    referenceList1: referenceList1);
            }

            IReadOnlyList<H264Mp4DecodedVideoFrame> readyFrames =
                _outputScheduler.Enqueue(
                    surface, sample, activeSps, firstSlice, firstNal.NalRefIdc, poc,
                    _maxNumReorderFramesFallback);
            _requiresReset = false;
            return new H264Mp4AccessUnitDecodeResult(
                sample, firstSlice, poc, surface, readyFrames);
        }
        catch
        {
            _requiresReset = true;
            throw;
        }
    }

    public IReadOnlyList<H264Mp4DecodedVideoFrame> Flush()
    {
        if (_requiresReset)
            throw new InvalidOperationException(
                "The decoder must be reset after a failed picture decode.");
        return _outputScheduler.Flush();
    }

    public void Reset()
    {
        _decodedPictureBuffer.Reset();
        _outputScheduler.Reset();
        _trackId = null;
        _timescale = null;
        _lastSampleNumber = null;
        _lastDecodeTimeTicks = null;
        _requiresReset = false;
    }

    private IH264CabacSliceDataReader CreateCabacReader(
        H264BitReader reader,
        int qpY,
        bool isIntra,
        int cabacInitIdc)
    {
        return _cabacReaderFactory(reader, qpY, isIntra, cabacInitIdc);
    }

    private void ValidateConfigurationIdentity(H264AvcDecoderConfiguration configuration)
    {
        H264AvcDecoderConfiguration expected = _parameterSets.Configuration;
        if (configuration.Profile != expected.Profile ||
            configuration.Compatibility != expected.Compatibility ||
            configuration.Level != expected.Level ||
            configuration.NaluLengthSize != expected.NaluLengthSize ||
            configuration.ChromaFormat != expected.ChromaFormat ||
            configuration.BitDepthLumaMinus8 != expected.BitDepthLumaMinus8 ||
            configuration.BitDepthChromaMinus8 != expected.BitDepthChromaMinus8 ||
            !NalUnitsMatch(configuration.SequenceParameterSets, expected.SequenceParameterSets) ||
            !NalUnitsMatch(configuration.PictureParameterSets, expected.PictureParameterSets) ||
            !NalUnitsMatch(configuration.SequenceParameterSetExtensions, expected.SequenceParameterSetExtensions))
            throw new FormatException(
                "The MP4 sample's avcC configuration does not match the decoder's active parameter-set collection.");
    }

    private static bool NalUnitsMatch(
        IReadOnlyList<H264NalUnit> left,
        IReadOnlyList<H264NalUnit> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].NalUnitType != right[i].NalUnitType ||
                left[i].NalRefIdc != right[i].NalRefIdc ||
                !left[i].Payload.Span.SequenceEqual(right[i].Payload.Span))
                return false;
        }
        return true;
    }

    private static void ValidateSliceHeader(H264NalUnit nalUnit, H264SliceHeader slice)
    {
        if (nalUnit.NalUnitType == 5 && nalUnit.NalRefIdc == 0)
            throw new FormatException("IDR NAL units must have non-zero nal_ref_idc.");
        if (nalUnit.NalUnitType == 5 && slice.SliceType != H264SliceType.I)
            throw new FormatException("IDR NAL units must contain I slices.");
        if (slice.FieldPicFlag || slice.ColourPlaneId != 0 || slice.RedundantPicCnt is > 0)
            throw new NotSupportedException(
                "Field, separate-plane and redundant slices are not supported.");
        if (slice.SliceType is H264SliceType.Sp or H264SliceType.Si)
            throw new NotSupportedException("SP and SI slice reconstruction is not supported.");
    }

    private static void ValidateSamePicture(
        H264NalUnit firstNal,
        H264SliceHeader first,
        H264NalUnit currentNal,
        H264SliceHeader current)
    {
        bool samePicture =
            firstNal.NalUnitType == currentNal.NalUnitType &&
            (firstNal.NalRefIdc == 0) == (currentNal.NalRefIdc == 0) &&
            first.SliceType == current.SliceType &&
            first.PicParameterSetId == current.PicParameterSetId &&
            first.ColourPlaneId == current.ColourPlaneId &&
            first.FrameNum == current.FrameNum &&
            first.FieldPicFlag == current.FieldPicFlag &&
            first.BottomFieldFlag == current.BottomFieldFlag &&
            first.IdrPicId == current.IdrPicId &&
            first.PicOrderCntLsb == current.PicOrderCntLsb &&
            first.DeltaPicOrderCntBottom == current.DeltaPicOrderCntBottom &&
            first.DeltaPicOrderCnt0 == current.DeltaPicOrderCnt0 &&
            first.DeltaPicOrderCnt1 == current.DeltaPicOrderCnt1 &&
            first.RedundantPicCnt == current.RedundantPicCnt &&
            ReferenceMarkingMatches(first.DecodedReferenceMarking, current.DecodedReferenceMarking);

        if (!samePicture)
            throw new FormatException("The MP4 sample contains VCL slices from different coded pictures.");
    }

    private static bool ReferenceMarkingMatches(
        H264DecodedReferenceMarking? left,
        H264DecodedReferenceMarking? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left.NoOutputOfPriorPicsFlag == right.NoOutputOfPriorPicsFlag &&
            left.LongTermReferenceFlag == right.LongTermReferenceFlag &&
            left.AdaptiveRefPicMarkingModeFlag == right.AdaptiveRefPicMarkingModeFlag &&
            left.Operations.SequenceEqual(right.Operations);
    }

    private static H264NalUnit[] GetSliceNals(IReadOnlyList<H264NalUnit> nalUnits)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        List<H264NalUnit> slices = [];
        foreach (H264NalUnit nalUnit in nalUnits)
        {
            switch (nalUnit.NalUnitType)
            {
                case 1:
                case 5:
                    slices.Add(nalUnit);
                    break;
                case 2:
                case 3:
                case 4:
                    throw new NotSupportedException("Partitioned AVC slice NAL units are not supported.");
                case 6:
                case 9:
                case 10:
                case 11:
                case 12:
                    // SEI, access-unit delimiter, end markers and filler do
                    // not carry macroblock data for this picture decoder.
                    break;
                case 7:
                case 8:
                case 13:
                    throw new NotSupportedException(
                        "In-band SPS/PPS updates and SPS extension NAL units are not supported; use the active avcC configuration.");
                default:
                    throw new NotSupportedException(
                        $"AVC NAL unit type {nalUnit.NalUnitType} is not supported by the MP4 access-unit decoder.");
            }
        }

        return slices.ToArray();
    }
}
