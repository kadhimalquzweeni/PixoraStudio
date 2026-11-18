using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes a single-slice AVC picture carried in one MP4 sample, using the
/// managed I/P/B slice decoders, frame DPB and MP4-aware POC output scheduler.
/// Multi-slice access units and in-band parameter-set changes are rejected.
/// </summary>
public sealed class H264Mp4AccessUnitDecoder
{
    private readonly H264AvcParameterSetCollection _parameterSets;
    private readonly H264DecodedPictureBuffer _decodedPictureBuffer = new();
    private readonly H264Mp4VideoOutputScheduler _outputScheduler = new();
    private readonly int? _maxNumReorderFramesFallback;
    private bool _requiresReset;

    public H264Mp4AccessUnitDecoder(
        H264AvcParameterSetCollection parameterSets,
        int? maxNumReorderFramesFallback = null)
    {
        ArgumentNullException.ThrowIfNull(parameterSets);
        if (maxNumReorderFramesFallback is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxNumReorderFramesFallback));
        _parameterSets = parameterSets;
        _maxNumReorderFramesFallback = maxNumReorderFramesFallback;
    }

    public H264DecodedPictureBuffer DecodedPictureBuffer => _decodedPictureBuffer;
    public int PendingOutputCount => _outputScheduler.PendingCount;

    /// <summary>
    /// Decodes one MP4 sample. Samples with no VCL NAL unit (for example,
    /// SEI/AUD-only samples) return null. Exactly one VCL slice is supported.
    /// </summary>
    public H264Mp4AccessUnitDecodeResult? Decode(
        H264Mp4VideoSample sample,
        int sliceId = 0)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (_requiresReset)
            throw new InvalidOperationException(
                "The decoder encountered a failed picture decode. Call Reset before decoding another sample.");
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if (sample.Timescale == 0)
            throw new FormatException("The MP4 AVC sample has an invalid timescale.");
        ValidateConfigurationIdentity(sample.Configuration);

        H264NalUnit[] slices = GetSliceNals(sample.NalUnits);
        if (slices.Length == 0)
            return null;
        if (slices.Length != 1)
            throw new NotSupportedException(
                "This MP4 access-unit decoder currently requires exactly one VCL slice per sample.");

        H264NalUnit nalUnit = slices[0];
        H264SliceHeaderReader sliceHeaderReader = new();
        (uint _, uint _, uint ppsId) =
            sliceHeaderReader.ReadLeadingIdentifiers(nalUnit);
        if (!_parameterSets.PictureParameterSets.TryGetValue(
            ppsId, out H264PictureParameterSetHeader? pps))
            throw new FormatException($"The AVC slice references missing PPS {ppsId}.");
        if (!_parameterSets.SequenceParameterSets.TryGetValue(
            pps.SequenceParameterSetId, out H264SequenceParameterSet? sps))
            throw new FormatException(
                $"PPS {pps.Id} references missing SPS {pps.SequenceParameterSetId}.");

        H264SliceHeader slice = sliceHeaderReader.Read(
            new H264BitReader(nalUnit.Rbsp), nalUnit, sps, pps);
        if (nalUnit.NalUnitType == 5 && slice.SliceType != H264SliceType.I)
            throw new FormatException("IDR NAL units must contain I slices.");
        if (slice.SliceType is H264SliceType.Sp or H264SliceType.Si)
            throw new NotSupportedException("SP and SI slice reconstruction is not supported.");
        if (nalUnit.NalRefIdc != 0 && sps.MaxNumRefFrames == 0)
            throw new FormatException("A reference slice cannot use an SPS with max_num_ref_frames equal to zero.");

        H264DecodedPictureSurface surface = new(sps);
        H264MotionVectorPictureState? motionStateL0 = null;
        H264MotionVectorPictureState? motionStateL1 = null;
        IReadOnlyList<H264ReferencePicture>? referenceList0 = null;
        IReadOnlyList<H264ReferencePicture>? referenceList1 = null;

        // POC is needed during B temporal-direct reconstruction. Once this
        // stateful calculation succeeds, any later failure faults this decoder
        // instance until Reset so subsequent samples cannot use a false epoch.
        _requiresReset = true;
        try
        {
            H264PictureOrderCount poc = _outputScheduler.CalculatePictureOrderCount(
                sps, slice, nalUnit.NalRefIdc);

            switch (slice.SliceType)
            {
                case H264SliceType.I:
                    _ = new H264IntraSliceDataDecoder(surface).Decode(
                        nalUnit, sps, pps, sliceId);
                    break;

                case H264SliceType.P:
                    motionStateL0 = new H264MotionVectorPictureState(
                        surface.MacroblockWidth, surface.MacroblockHeight);
                    motionStateL1 = new H264MotionVectorPictureState(
                        surface.MacroblockWidth, surface.MacroblockHeight);
                    H264PInterSliceDecodeResult pResult =
                        new H264PInterSliceDataDecoder(surface, motionStateL0).Decode(
                            nalUnit, sps, pps, _decodedPictureBuffer, sliceId);
                    referenceList0 = pResult.ReferenceList0 ??
                        throw new InvalidOperationException("The P-slice decoder did not return its reference list.");
                    referenceList1 = [];
                    break;

                case H264SliceType.B:
                    motionStateL0 = new H264MotionVectorPictureState(
                        surface.MacroblockWidth, surface.MacroblockHeight);
                    motionStateL1 = new H264MotionVectorPictureState(
                        surface.MacroblockWidth, surface.MacroblockHeight);
                    H264BInterSliceDecodeResult bResult =
                        new H264BInterSliceDataDecoder(
                            surface, motionStateL0, motionStateL1).Decode(
                                nalUnit, sps, pps, _decodedPictureBuffer,
                                poc.PictureOrderCount, sliceId);
                    H264BReferencePictureLists lists = bResult.ReferencePictureLists ??
                        throw new InvalidOperationException("The B-slice decoder did not return its reference lists.");
                    referenceList0 = lists.List0;
                    referenceList1 = lists.List1;
                    break;

                default:
                    throw new NotSupportedException(
                        $"Slice type {slice.SliceType} is not supported by the MP4 access-unit decoder.");
            }

            if (!surface.IsComplete)
                throw new NotSupportedException(
                    "The single VCL slice did not reconstruct the complete picture; multi-slice access units are not supported.");

            if (nalUnit.NalRefIdc != 0)
            {
                int maxFrameNum = 1 << checked((int)sps.Log2MaxFrameNumMinus4 + 4);
                _decodedPictureBuffer.StoreDecodedPicture(
                    surface,
                    new H264ReferencePicture(checked((int)slice.FrameNum), poc.PictureOrderCount),
                    isReference: true,
                    sps.MaxNumRefFrames,
                    checked((int)slice.FrameNum),
                    maxFrameNum,
                    slice.DecodedReferenceMarking,
                    idrPicture: nalUnit.NalUnitType == 5,
                    motionVectorStateL0: motionStateL0,
                    motionVectorStateL1: motionStateL1,
                    referenceList0: referenceList0,
                    referenceList1: referenceList1);
            }

            IReadOnlyList<H264Mp4DecodedVideoFrame> readyFrames =
                _outputScheduler.Enqueue(
                    surface, sample, sps, slice, nalUnit.NalRefIdc, poc,
                    _maxNumReorderFramesFallback);
            _requiresReset = false;
            return new H264Mp4AccessUnitDecodeResult(
                sample, slice, poc, surface, readyFrames);
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
        _requiresReset = false;
    }

    private void ValidateConfigurationIdentity(H264AvcDecoderConfiguration configuration)
    {
        H264AvcDecoderConfiguration expected = _parameterSets.Configuration;
        if (configuration.Profile != expected.Profile ||
            configuration.Compatibility != expected.Compatibility ||
            configuration.Level != expected.Level ||
            configuration.NaluLengthSize != expected.NaluLengthSize)
            throw new FormatException(
                "The MP4 sample's avcC configuration does not match the decoder's active parameter-set collection.");
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
