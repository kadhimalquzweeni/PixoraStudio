using System.Buffers.Binary;
using System.Security.Cryptography;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4AvcEndToEndTests
{
    [Fact]
    public void ReadMp4Sample_ThenDecodeIdrPcmSlice_ShouldProduceExpectedPicture()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        byte[] sliceNal = BuildNal(0x65, CreateIdrPcmSliceRbsp());

        List<byte> avcC =
        [
            1, 66, 0, 30, 0xFD, 0xE1
        ];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4CodecConfiguration codecConfiguration = new(
            Mp4VideoCodec.Avc, "avcC", avcC.ToArray(),
            1, 66, 0, 30, 2);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc, codecConfiguration);
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, sliceNal);
        Mp4EncodedSample encodedSample = new(
            new Mp4SampleLocation(1, 1, 0, (uint)sampleData.Count, 0, 40, true)
            {
                SampleDescriptionIndex = 1
            },
            sampleData.ToArray());

        H264Mp4VideoSample mp4Sample =
            new H264Mp4TrackSampleReader().Read(track, encodedSample);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(mp4Sample.Configuration);
        H264SequenceParameterSet sps = Assert.Single(parameterSets.SequenceParameterSets).Value;
        Assert.Equal(0u, Assert.Single(parameterSets.PictureParameterSets).Value.Id);

        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);
        H264Mp4AccessUnitDecodeResult decodeResult = decoder.Decode(mp4Sample)!;
        H264DecodedPictureSurface surface = decodeResult.Surface;

        Assert.Equal(H264SliceType.I, decodeResult.SliceHeader.SliceType);
        Assert.Equal(0, decodeResult.PictureOrderCount.PictureOrderCount);
        Assert.True(surface.IsComplete);
        Assert.True(surface.HasCompleteDeblockingMetadata);
        Assert.Equal(16u, sps.FrameWidth);
        Assert.Equal(16u, sps.FrameHeight);
        Assert.All(surface.CopyLumaPlane(), sample => Assert.Equal((byte)80, sample));
        Assert.All(surface.CopyCbPlane(), sample => Assert.Equal((byte)96, sample));
        Assert.All(surface.CopyCrPlane(), sample => Assert.Equal((byte)160, sample));
        H264Mp4DecodedVideoFrame output = Assert.Single(decodeResult.ReadyFrames);
        Assert.Equal(9u, output.TrackId);
        Assert.Equal(1u, output.SampleNumber);
        Assert.Equal(0L, output.PresentationTimeTicks);
        Assert.Equal(1000u, output.Timescale);
        Assert.Equal(0.0, mp4Sample.DecodeTimeSeconds, 6);
        Assert.Equal(0.04, mp4Sample.DurationSeconds, 6);
        Assert.True(mp4Sample.IsSyncSample);
        Assert.Single(decoder.DecodedPictureBuffer.Entries);
        Assert.Empty(decoder.Flush());

        H264Mp4DecodedVideoFrame trackFrame = Assert.Single(
            new H264Mp4TrackDecoder().DecodeSamples(
                track, [encodedSample], maxNumReorderFramesFallback: 0));
        Assert.Equal(1u, trackFrame.SampleNumber);
        Assert.Equal(0, trackFrame.PictureOrderCount);
        Assert.All(trackFrame.Surface.CopyLumaPlane(),
            sample => Assert.Equal((byte)80, sample));
    }


    [Fact]
    public void DecodeSamples_CabacMainProfileIAndPBTrack_ShouldMatchReferenceDecoder()
    {
        byte[] avcC = Convert.FromBase64String(CabacMainAvcC);
        H264AvcDecoderConfiguration configuration =
            new H264AvcDecoderConfigurationReader().Read(avcC);
        Mp4CodecConfiguration codecConfiguration = new(
            Mp4VideoCodec.Avc, "avcC", avcC, 1,
            configuration.Profile, configuration.Compatibility,
            configuration.Level, configuration.NaluLengthSize);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 32, 32, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc, codecConfiguration);
        Mp4Track track = new(
            9, 32, 32, 1, 3, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        Mp4EncodedSample[] samples =
        [
            CreateCabacEncodedSample(1, 0, 0, true, CabacMainIdrNal),
            CreateCabacEncodedSample(2, 1, 2, false, CabacMainPNal),
            CreateCabacEncodedSample(3, 2, 1, false, CabacMainBNal)
        ];

        H264Mp4DecodedVideoFrame[] frames =
            new H264Mp4TrackDecoder().DecodeSamples(
                track, samples, maxNumReorderFramesFallback: 2).ToArray();

        Assert.Equal(new uint[] { 1, 3, 2 },
            frames.Select(frame => frame.SampleNumber));
        Assert.Equal(new long[] { 0, 1, 2 },
            frames.Select(frame => frame.PresentationTimeTicks));
        Assert.Equal(new[] { 0, 2, 4 },
            frames.Select(frame => frame.PictureOrderCount));
        Assert.All(frames, frame => Assert.True(frame.Surface.IsComplete));
        Assert.All(frames, frame =>
            Assert.True(frame.Surface.HasCompleteDeblockingMetadata));

        string[] expectedFrameHashes =
        [
            "fced2e3477559d7819c0d5103914b4083521dd813345ed76b173819584c3e639",
            "9884d66af8fd0c7362e124d32535382232ac3f6147c3aac2253ab904bc946303",
            "e65b186f669f54288e84fe4dfd3b8891a0f1b01109110a999d1ee511d5cc3a9c"
        ];
        Assert.Equal(expectedFrameHashes, frames.Select(ComputeFrameHash));
    }

    [Fact]
    public void DecodeSamples_CabacMainProfileMultiMacroblockBResidual_ShouldComplete()
    {
        byte[] avcC = Convert.FromBase64String(CabacMainResidual32AvcC);
        H264AvcDecoderConfiguration configuration =
            new H264AvcDecoderConfigurationReader().Read(avcC);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(configuration);
        List<string> trace = [];
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets,
            2,
            (reader, qpY, isIntra, cabacInitIdc) =>
                new TracingCabacSliceDataReader(
                    new H264CabacSliceDataReader(reader, qpY, isIntra, cabacInitIdc),
                    trace));

        Assert.NotNull(decoder.Decode(CreateCabacSample(
            configuration, 1, 0, 0, true, CabacMainResidual32IdrNal)));
        Assert.NotNull(decoder.Decode(CreateCabacSample(
            configuration, 2, 1, 2, false, CabacMainResidual32PNal)));
        H264Mp4AccessUnitDecodeResult bResult = decoder.Decode(CreateCabacSample(
            configuration, 3, 2, 1, false, CabacMainResidual32BNal))!;

        Assert.Equal(H264SliceType.B, bResult.SliceHeader.SliceType);
        Assert.Contains(trace, entry =>
            entry.Contains("stage=residual-syntax complete cbp_luma=15"));
        Assert.Contains(trace, entry =>
            entry.Contains("stage=residual-coefficients complete"));
        Assert.Contains(trace, entry =>
            entry.Contains("stage=end_of_slice_flag value=1"));
    }

    [Fact]
    public void DecodeSamples_HighProfileCabac8x8IAndPBTrack_ShouldMatchReferenceDecoder()
    {
        byte[] avcC = Convert.FromBase64String(CabacHighAvcC);
        H264AvcDecoderConfiguration configuration =
            new H264AvcDecoderConfigurationReader().Read(avcC);
        Assert.Equal((byte)100, configuration.Profile);
        Assert.Equal((byte)1, configuration.ChromaFormat);
        Assert.Equal((byte)0, configuration.BitDepthLumaMinus8);
        Assert.Equal((byte)0, configuration.BitDepthChromaMinus8);

        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(configuration);
        Assert.True(parameterSets.PictureParameterSets[0].Transform8X8ModeFlag);

        List<string> highProfileCabacTrace = [];
        H264Mp4AccessUnitDecoder diagnosticDecoder = new(
            parameterSets,
            2,
            (reader, qpY, isIntra, cabacInitIdc) =>
                new TracingCabacSliceDataReader(
                    new H264CabacSliceDataReader(reader, qpY, isIntra, cabacInitIdc),
                    highProfileCabacTrace));
        string testResultsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../TestResults"));
        Directory.CreateDirectory(testResultsDirectory);
        try
        {
            diagnosticDecoder.Decode(CreateCabacSample(
                configuration, 1, 0, 0, true, CabacHighIdrNal));
            diagnosticDecoder.Decode(CreateCabacSample(
                configuration, 2, 1, 2, false, CabacHighPNal));
            diagnosticDecoder.Decode(CreateCabacSample(
                configuration, 3, 2, 1, false, CabacHighBNal, trailingZeroBytes: 16));
        }
        finally
        {
            File.WriteAllLines(
                Path.Combine(testResultsDirectory, "cabac-high-profile-idr-trace.txt"),
                highProfileCabacTrace);
        }

        Mp4CodecConfiguration codecConfiguration = new(
            Mp4VideoCodec.Avc, "avcC", avcC, 1,
            configuration.Profile, configuration.Compatibility,
            configuration.Level, configuration.NaluLengthSize);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 32, 32, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc, codecConfiguration);
        Mp4Track track = new(
            9, 32, 32, 1, 3, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        Mp4EncodedSample[] samples =
        [
            CreateCabacEncodedSample(1, 0, 0, true, CabacHighIdrNal),
            CreateCabacEncodedSample(2, 1, 2, false, CabacHighPNal),
            CreateCabacEncodedSample(3, 2, 1, false, CabacHighBNal, trailingZeroBytes: 16)
        ];

        H264Mp4DecodedVideoFrame[] frames =
            new H264Mp4TrackDecoder().DecodeSamples(
                track, samples, maxNumReorderFramesFallback: 2).ToArray();

        Assert.Equal(new uint[] { 1, 3, 2 },
            frames.Select(frame => frame.SampleNumber));
        Assert.Equal(new long[] { 0, 1, 2 },
            frames.Select(frame => frame.PresentationTimeTicks));
        Assert.Equal(new[] { 0, 2, 4 },
            frames.Select(frame => frame.PictureOrderCount));
        Assert.All(frames, frame => Assert.True(frame.Surface.IsComplete));
        Assert.All(frames, frame =>
            Assert.True(frame.Surface.HasCompleteDeblockingMetadata));

        // Reference frames were decoded from the same encoded MP4 using FFmpeg 7.1.
        string[] expectedFrameHashes =
        [
            "74d1870d8599bd3cd340270b7c04a2e920d8f39433f67e3dd764d9ff4242b7c1",
            "fb67b186d4dff36ad4809f9c20290b5afc4eab4924eeb4ef763010bda2f83558",
            "115c74e0cfd5e28fa92c26fe0aa647f1f8d4ea4705606e6c15b7f81f33ed205b"
        ];
        Assert.Equal(expectedFrameHashes, frames.Select(ComputeFrameHash));
    }

    [Fact]
    public void Decode_CabacMainProfileIAndPBStream_ShouldMatchReferenceDecoder()
    {
        // Three 32x32 Main-profile pictures encoded with x264 CABAC:
        // IDR I, reference P, then B in decode order. The tiny fixture keeps
        // real arithmetic-coded slice data in the test suite without an
        // encoder or FFmpeg runtime dependency.
        H264AvcDecoderConfiguration configuration =
            new H264AvcDecoderConfigurationReader().Read(
                Convert.FromBase64String(CabacMainAvcC));
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(configuration);
        List<string> cabacTrace = [];
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets,
            2,
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                cabacTrace.Add($"slice-start intra={isIntra} qp={qpY} init={cabacInitIdc}");
                H264CabacSliceDataReader sliceReader =
                    new(reader, qpY, isIntra, cabacInitIdc);
                cabacTrace.Add(
                    $"cabac-start range={sliceReader.ArithmeticRange} " +
                    $"offset={sliceReader.ArithmeticOffset} bitpos={sliceReader.BitPosition}");
                return new TracingCabacSliceDataReader(sliceReader, cabacTrace);
            });

        H264Mp4VideoSample idr = CreateCabacSample(
            configuration, 1, 0, 0, true, CabacMainIdrNal);
        H264Mp4VideoSample p = CreateCabacSample(
            configuration, 2, 1, 2, false, CabacMainPNal);
        H264Mp4VideoSample b = CreateCabacSample(
            configuration, 3, 2, 1, false, CabacMainBNal);

        string testResultsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../TestResults"));
        Directory.CreateDirectory(testResultsDirectory);

        List<H264Mp4DecodedVideoFrame> frames = [];
        frames.AddRange(decoder.Decode(idr)!.ReadyFrames);
        File.WriteAllLines(
            Path.Combine(testResultsDirectory, "cabac-idr-bin-trace.txt"),
            cabacTrace);
        string? idrHashWhenFirstReleased = null;
        cabacTrace.Clear();
        try
        {
            H264Mp4DecodedVideoFrame[] pReadyFrames = decoder.Decode(p)!.ReadyFrames.ToArray();
            H264Mp4DecodedVideoFrame? idrReleasedAfterP =
                pReadyFrames.FirstOrDefault(frame => frame.SampleNumber == 1);
            if (idrReleasedAfterP is not null)
                idrHashWhenFirstReleased = ComputeFrameHash(idrReleasedAfterP);
            frames.AddRange(pReadyFrames);
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[0] header skip=False intra=False pcm=False mb_type=0"));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[0] stage=inter-prediction-syntax complete"));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[0] stage=residual-coefficients complete"));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[1] header skip=True"));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[2] header skip=True"));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[3] header skip=True"));
            Assert.DoesNotContain(cabacTrace, entry => entry.Contains("sub_mb_type["));
            Assert.Contains(cabacTrace, entry =>
                entry.Contains("mb[3] stage=end_of_slice_flag value=1"));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                exception.Message + Environment.NewLine + string.Join(Environment.NewLine, cabacTrace),
                exception);
        }
        cabacTrace.Clear();
        H264Mp4AccessUnitDecodeResult bResult = decoder.Decode(b)!;
        H264Mp4DecodedVideoFrame[] bReadyFrames = bResult.ReadyFrames.ToArray();
        File.WriteAllLines(
            Path.Combine(testResultsDirectory, "cabac-b-slice-trace.txt"),
            cabacTrace);

        // The real CABAC B-slice fixture must traverse inter prediction,
        // coded-block-pattern/QP syntax, residual coefficients and final
        // reconstruction through the MP4 access-unit pipeline.
        Assert.Equal(H264SliceType.B, bResult.SliceHeader.SliceType);
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("mb[0] header skip=False intra=False pcm=False mb_type=2"));
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("mb[0] stage=inter-prediction-syntax complete") &&
            entry.Contains("directions=List1"));
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("mb[0] stage=residual-syntax complete cbp_luma=15 cbp_chroma=2"));
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("mb[0] stage=residual-coefficients complete"));
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("mb[0] stage=reconstruction complete"));
        Assert.Contains(cabacTrace, entry =>
            entry.Contains("stage=end_of_slice_flag value=1"));

        H264Mp4DecodedVideoFrame? idrReleasedAfterB =
            bReadyFrames.FirstOrDefault(frame => frame.SampleNumber == 1);
        if (idrHashWhenFirstReleased is null && idrReleasedAfterB is not null)
            idrHashWhenFirstReleased = ComputeFrameHash(idrReleasedAfterB);
        frames.AddRange(bReadyFrames);

        H264Mp4DecodedVideoFrame[] flushedFrames = decoder.Flush().ToArray();
        H264Mp4DecodedVideoFrame? idrReleasedOnFlush =
            flushedFrames.FirstOrDefault(frame => frame.SampleNumber == 1);
        if (idrHashWhenFirstReleased is null && idrReleasedOnFlush is not null)
            idrHashWhenFirstReleased = ComputeFrameHash(idrReleasedOnFlush);
        frames.AddRange(flushedFrames);

        Assert.Equal(3, frames.Count);
        H264Mp4DecodedVideoFrame[] displayOrder = frames
            .OrderBy(frame => frame.PresentationTimeTicks)
            .ToArray();
        Assert.Equal(new uint[] { 1, 3, 2 },
            displayOrder.Select(frame => frame.SampleNumber));
        Assert.Equal(new[] { 0, 2, 4 },
            displayOrder.Select(frame => frame.PictureOrderCount));
        Assert.All(displayOrder, frame => Assert.True(frame.Surface.IsComplete));
        Assert.All(displayOrder, frame =>
            Assert.True(frame.Surface.HasCompleteDeblockingMetadata));

        string[] expectedFrameHashes =
        [
            "fced2e3477559d7819c0d5103914b4083521dd813345ed76b173819584c3e639",
            "9884d66af8fd0c7362e124d32535382232ac3f6147c3aac2253ab904bc946303",
            "e65b186f669f54288e84fe4dfd3b8891a0f1b01109110a999d1ee511d5cc3a9c"
        ];
        string[] actualFrameHashes = displayOrder.Select(ComputeFrameHash).ToArray();

        byte[] decodedYuvFrames = displayOrder
            .SelectMany(frame => frame.Surface.CopyLumaPlane()
                .Concat(frame.Surface.CopyCbPlane())
                .Concat(frame.Surface.CopyCrPlane()))
            .ToArray();
        File.WriteAllBytes(
            Path.Combine(testResultsDirectory, "cabac-decoded-frames.yuv"),
            decodedYuvFrames);

        Assert.True(
            expectedFrameHashes.SequenceEqual(actualFrameHashes),
            $"IDR hash when first released: {idrHashWhenFirstReleased ?? "not released"}{Environment.NewLine}" +
            $"Expected: {string.Join(", ", expectedFrameHashes)}{Environment.NewLine}" +
            $"Actual:   {string.Join(", ", actualFrameHashes)}");
    }


    [Fact]
    public void Decode_CabacPOnlySkipStream_ShouldDecodeAllPictures()
    {
        H264AvcDecoderConfiguration configuration =
            new H264AvcDecoderConfigurationReader().Read(
                Convert.FromBase64String(CabacPSkipAvcC));
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(configuration);
        List<string> cabacTrace = [];
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets,
            0,
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                cabacTrace.Add($"slice-start intra={isIntra} qp={qpY} init={cabacInitIdc}");
                return new TracingCabacSliceDataReader(
                    new H264CabacSliceDataReader(reader, qpY, isIntra, cabacInitIdc),
                    cabacTrace);
            });

        H264Mp4VideoSample idr = CreateCabacSample(
            configuration, 1, 0, 0, true, CabacPSkipIdrNal);
        H264Mp4VideoSample p1 = CreateCabacSample(
            configuration, 2, 1, 1, false, CabacPSkipP1Nal);
        H264Mp4VideoSample p2 = CreateCabacSample(
            configuration, 3, 2, 2, false, CabacPSkipP2Nal);

        List<H264Mp4DecodedVideoFrame> frames = [];
        frames.AddRange(decoder.Decode(idr)!.ReadyFrames);
        frames.AddRange(decoder.Decode(p1)!.ReadyFrames);
        frames.AddRange(decoder.Decode(p2)!.ReadyFrames);
        frames.AddRange(decoder.Flush());

        Assert.Equal(new uint[] { 1, 2, 3 },
            frames.OrderBy(frame => frame.PresentationTimeTicks)
                .Select(frame => frame.SampleNumber));
        Assert.All(frames, frame => Assert.True(frame.Surface.IsComplete));
        Assert.All(frames, frame =>
        {
            byte[] samples = frame.Surface.CopyLumaPlane()
                .Concat(frame.Surface.CopyCbPlane())
                .Concat(frame.Surface.CopyCrPlane())
                .ToArray();
            string actualHash =
                Convert.ToHexString(SHA256.HashData(samples)).ToLowerInvariant();
            string diagnostics =
                $"Y={string.Join(",", frame.Surface.CopyLumaPlane().Distinct().Order())}; " +
                $"Cb={string.Join(",", frame.Surface.CopyCbPlane().Distinct().Order())}; " +
                $"Cr={string.Join(",", frame.Surface.CopyCrPlane().Distinct().Order())}; " +
                $"Y0={string.Join(",", frame.Surface.CopyLumaPlane().Take(32))}; " +
                $"CABAC={string.Join(" | ", cabacTrace)}";
            Assert.True(
                actualHash == "df7fa6b29395ce3a6fcfb65d7b3d6fc3af2889c58bdddc1c412622c14da81595",
                $"Frame {frame.SampleNumber} hash {actualHash}. {diagnostics}");
        });
    }

    [Fact]
    public void ReadMp4Samples_ShouldDecodeBDirectPictureBetweenPastAndFutureReferences()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp(maxNumRefFrames: 2));
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true,
            BuildNal(0x65, CreateIdrPcmSliceRbsp(luma: 80, cb: 96, cr: 160)));
        H264Mp4VideoSample futurePSample = ReadSample(
            track, 2, 40, false,
            BuildNal(0x61, CreatePcmPSliceRbsp(luma: 120, cb: 140, cr: 200)),
            presentationTimeTicks: 80);
        H264Mp4VideoSample bSample = ReadSample(
            track, 3, 80, false,
            BuildNal(0x01, CreateBDirectSliceRbsp()),
            presentationTimeTicks: 40);

        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 2);

        H264Mp4AccessUnitDecodeResult idrResult = decoder.Decode(idrSample)!;
        Assert.Empty(idrResult.ReadyFrames);
        H264Mp4AccessUnitDecodeResult pResult = decoder.Decode(futurePSample)!;
        Assert.Empty(pResult.ReadyFrames);
        H264Mp4AccessUnitDecodeResult bResult = decoder.Decode(bSample)!;
        Assert.All(pResult.Surface.CopyLumaPlane(), sample => Assert.Equal((byte)120, sample));
        Assert.All(pResult.Surface.CopyCbPlane(), sample => Assert.Equal((byte)140, sample));
        Assert.All(pResult.Surface.CopyCrPlane(), sample => Assert.Equal((byte)200, sample));

        H264Mp4DecodedVideoFrame firstOutput = Assert.Single(bResult.ReadyFrames);
        Assert.Equal(1u, firstOutput.SampleNumber);
        Assert.Equal(0, firstOutput.PictureOrderCount);
        Assert.Equal(0L, firstOutput.PresentationTimeTicks);
        Assert.Equal(H264SliceType.B, bResult.SliceHeader.SliceType);
        Assert.All(bResult.Surface.CopyLumaPlane(), sample => Assert.Equal((byte)100, sample));
        Assert.All(bResult.Surface.CopyCbPlane(), sample => Assert.Equal((byte)118, sample));
        Assert.All(bResult.Surface.CopyCrPlane(), sample => Assert.Equal((byte)180, sample));

        IReadOnlyList<H264Mp4DecodedVideoFrame> remaining = decoder.Flush();
        Assert.Equal(new[] { 3u, 2u }, remaining.Select(frame => frame.SampleNumber));
        Assert.Equal(new[] { 2, 4 }, remaining.Select(frame => frame.PictureOrderCount));
        Assert.Equal(new long[] { 40, 80 }, remaining.Select(frame => frame.PresentationTimeTicks));
        Assert.Equal(2, decoder.DecodedPictureBuffer.Count);
    }

    [Fact]
    public void ReadMp4Samples_ShouldDecodePReferencePictureUsingPreviousIdrSurface()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        byte[] idrNal = BuildNal(0x65, CreateIdrPcmSliceRbsp());
        byte[] pNal = BuildNal(0x61, CreatePSkipSliceRbsp());
        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true, idrNal);
        H264Mp4VideoSample pSample = ReadSample(
            track, 2, 40, false, pNal);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);

        H264Mp4AccessUnitDecodeResult idrResult = decoder.Decode(idrSample)!;
        H264Mp4DecodedVideoFrame idrOutput = Assert.Single(idrResult.ReadyFrames);
        Assert.Equal(0, idrOutput.PictureOrderCount);

        H264Mp4AccessUnitDecodeResult pResult = decoder.Decode(pSample)!;
        H264Mp4DecodedVideoFrame pOutput = Assert.Single(pResult.ReadyFrames);

        Assert.Equal(H264SliceType.P, pResult.SliceHeader.SliceType);
        Assert.Equal(2, pOutput.PictureOrderCount);
        Assert.Equal(2u, pOutput.SampleNumber);
        Assert.Equal(idrResult.Surface.CopyLumaPlane(), pResult.Surface.CopyLumaPlane());
        Assert.Equal(idrResult.Surface.CopyCbPlane(), pResult.Surface.CopyCbPlane());
        Assert.Equal(idrResult.Surface.CopyCrPlane(), pResult.Surface.CopyCrPlane());
        Assert.Single(decoder.DecodedPictureBuffer.Entries);
        Assert.Equal(1, decoder.DecodedPictureBuffer.Entries[0].Picture.FrameNum);
    }

    [Fact]
    public void Decode_WhenSampleNumberIsSkipped_ShouldRejectGapWithoutAdvancingDecoder()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true, BuildNal(0x65, CreateIdrPcmSliceRbsp()));
        H264Mp4VideoSample skippedSample = ReadSample(
            track, 3, 80, false, BuildNal(0x61, CreatePSkipSliceRbsp()));
        H264Mp4VideoSample nextSample = ReadSample(
            track, 2, 40, false, BuildNal(0x61, CreatePSkipSliceRbsp()));

        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);
        _ = decoder.Decode(idrSample);

        Assert.Throws<FormatException>(() => decoder.Decode(skippedSample));

        H264Mp4AccessUnitDecodeResult result = decoder.Decode(nextSample)!;
        Assert.Equal(2u, Assert.Single(result.ReadyFrames).SampleNumber);
        Assert.Equal(2, result.PictureOrderCount.PictureOrderCount);
    }

    [Fact]
    public void Decode_WhenTrackTimescaleChanges_ShouldRejectSample()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);
        H264Mp4VideoSample sample = ReadSample(
            track, 1, 0, true, BuildNal(0x65, CreateIdrPcmSliceRbsp()));
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(sample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);
        _ = decoder.Decode(sample);

        Assert.Throws<FormatException>(() =>
            decoder.Decode(sample with { SampleNumber = 2, Timescale = 2000 }));
    }

    [Fact]
    public void ReadMp4Sample_WithTwoSlices_ShouldAssembleOneCompletePicture()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp(picWidthInMbsMinus1: 1));
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        byte[] firstSlice = BuildNal(0x65,
            CreateIdrPcmSliceRbsp(firstMacroblock: 0, luma: 80, cb: 96, cr: 160));
        byte[] secondSlice = BuildNal(0x65,
            CreateIdrPcmSliceRbsp(firstMacroblock: 1, luma: 120, cb: 140, cr: 200));

        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 32, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 32, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, firstSlice);
        AddLengthPrefixedNal(sampleData, secondSlice);
        Mp4EncodedSample encodedSample = new(
            new Mp4SampleLocation(1, 1, 0, (uint)sampleData.Count, 0, 40, true)
            {
                SampleDescriptionIndex = 1
            },
            sampleData.ToArray());

        H264Mp4VideoSample mp4Sample =
            new H264Mp4TrackSampleReader().Read(track, encodedSample);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(mp4Sample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);

        H264Mp4AccessUnitDecodeResult result = decoder.Decode(mp4Sample)!;

        Assert.Equal(2, result.Surface.MacroblockWidth);
        Assert.True(result.Surface.IsComplete);
        Assert.All(result.Surface.CopyLumaPlane().Select((value, index) =>
            (value, x: index % 32)), sample =>
        {
            Assert.Equal(sample.x < 16 ? (byte)80 : (byte)120, sample.value);
        });
        Assert.All(result.Surface.CopyCbPlane().Select((value, index) =>
            (value, x: index % 16)), sample =>
        {
            Assert.Equal(sample.x < 8 ? (byte)96 : (byte)140, sample.value);
        });
        Assert.All(result.Surface.CopyCrPlane().Select((value, index) =>
            (value, x: index % 16)), sample =>
        {
            Assert.Equal(sample.x < 8 ? (byte)160 : (byte)200, sample.value);
        });
        Assert.Single(result.ReadyFrames);
    }



    private const string CabacPSkipAvcC =
        "AU1ACv/hABVnTUAK2nsBEAAAAwAQAAADAGDxImoBAARo7g/I";
    private const string CabacPSkipIdrNal = "ZYiEOv7tz7/h";
    private const string CabacPSkipP1Nal = "QZoiCNf+4A==";
    private const string CabacPSkipP2Nal = "QZpCCNf+4Q==";

    private const string CabacMainResidual32AvcC =
        "AU1ACv/hABZnTUAK9ktgIgAAAwACAAADAAQeJEpwAQAFaO+CMsg=";
    private const string CabacMainResidual32IdrNal =
        "ZYiEA//F9iVJ02WIOfaIfQOzovJyTd5V0cw9QngBmUJqnS2PZJIfJP3cnQ2DRmmO9wCDSOZxv4DtixWhtI6WBuDEuPsZ9jgCPTKvqjmNDk4mY5FIFf0b7z6MZQ+r2nXVYxOr3dS7k1xQNd9T8844fewvVIPyyTFnHr9aa9BkzLpm1xoljmPaS01e14MusjNyMy36C+FAz4VHjKc6TEyKP5+NN8MNjlkWErZKx6UjtFGhMQNBgiucuczEIRe+ft5E9b7FY1wu0RYz2Z0yCEzgDrWRspotp4WfM+nvUthez5LJ1i/cmzjhKKEzBAHpKAIlINBJ6c1z0+EafF9By1meInx1oxdmyFQ7Ufmd3MEejyxQmpj0JvUennL3b4PX4HAV1DsEVRhSPYDz1kFeNX1ITMnGNQhNo4SCYEpmf98o1bDBH65JQ2Up1obEeQwAkkni5Js9eBZgp/HVvXOurfKuqa2hOn3ihXIJo1Nv0ZhfMbua4ekFFqAz1xXbz1ze4sofcBIWSGlGRsN/H2wVTsdmBawL3vWzzyfIb7V+YCk5ePyfe7ECZeeejZd/gG85cEd/X28nFG532Yo+m4D1v/rOH5561P4hgF510ejxvZZ/uVmxh++h+D7+rY5uDZJDSQtJZAiWn3Q3nQVu2sRA54/ZvVt086DzjDHsLZZO1jCFFo4xsVhsbEYEZbABKMpq2a0D9/OFRngg9WYtvH+1gpYTJ3CBcAdQWlYdbCmB";
    private const string CabacMainResidual32PNal =
        "QZooY/9yoF4zzY16GSB3PotxZVpBk5r+pqTPwOdzGXytUe2FWi68k6w/oXrH7qVvunAwGPDk53CkLbFLUTwcB+fYGijqugWyyPnGAin6dgoT+dCGNvl6J/B8WodgLN22doK/vrhaesmyjB/NBguKB2LC6g9Cp09rXvsDbRC7U6VyKXmPSjrn//1FT5t08gGf2ItfecHayLsVnRQlD0waipOzNcaPN5q3WGZoollgMjEnA5zA";
    private const string CabacMainResidual32BNal =
        "AZ5EH/VW/rH3/o7dTwhXWxlhOJVROTvyxIkrRMKt2epNZ+6hmDPaOgYm9aO+QylvtAtJP6ut8UGHIfkt84fzOr1YI2KVla8WVsmuAienr0RR";

    private const string CabacHighAvcC =
        "AWQACv/hABdnZAAKrOyWwEQAAAMABAAAAwAIPEiU4AEABmjvgjLIsP34+AA=";
    private const string CabacHighIdrNal =
        "ZYiEA/+XtxKk6bLEHPtEPoHZ0Xk5Ju8q6OYeoTwAzKE1TpbHskkPkn7uTobBozTHe4BBpHM438B2xYrQ2kdLA3BiXH2M+xwBHplX1RzGhycTMcikCv6N959GMofV7TrqsYnV7updya4oGu+p+eccPvYXqkH5ZJizj1+tNegyZl0za40SxzHtJaava8GXWRm5GZb9BfCgZ8KjxlOdJiZFH8/Gm+GGxyyLCVslY9KR2ijQmIGgwRXOXOZiEIvfP28iet9isa4XaIsZ7M6ZBCZwB1rI2U0W08LPmfT3qWwvZ8lk6xfuTZxwlFCZggD0lAESb9uR1XSNTPhGnxfQctZniJ8daMXZshUO1H5ndzBHo8sUJqY9Cb1Hp5y92+D1+BwFdQ7BFUYUj2A89ZBXjV9SEzJxjUITaOEgmBKZn/fKNWwwR+uSUNlKdaGxHkMAJJJ4uSbPXgWYKfx1b1zrq3yrqmtoTp94oVyCaNTb9GYXzG7muHpBRagM9cV289c3uLKH3ASFkhpRkbDfx9sFU7HZgWsC971s88nyG+1fmApOXj8n3uxAmXnno2Xf4BvOXBHf19vJxRud9mKPpuA9b/6zh+eetT+IYBeddHo8b2Wf7lZq16d/pIvF62Obg2SQ0kLSWQIlp90N50FbtrEQOeP2b1bdPOg84wx7C2WTtYwhRaOMbFYbGwhyaWwASjKatmtA/fzhUZ4IPVmLbx/tYKWEydwgXAHUFpWHWwph";
    private const string CabacHighPNal =
        "QZooY/9yn0SPzY16GSB3PotxZVpBk5r+pqTPwOdzGXytUe2FWi68k6w/oXrH7qVvunAwGPDk53CkLbFLUTwcB+fYGijqugWyyPnGAin6dgoT+dCGNvl6J/B8WodgLN22doK/vrhaesmyjB/NBguKB2LC6g9Cp09rXvsDbRC7U6VyKXmPSjrn//1FT5t08gGfZotfecHayLsVnRQlD0waipOzNcaPN5q3WGZoollgMjEnA5zA";
    private const string CabacHighBNal =
        "AZ5EH/VWm9AhsQdup4QrrYywnEqonJ35YkSVomFW7PUms/dQzBntHQMTetHfIZS32gWkn9XW+KDDkPyW+cP5nV6sEbFKyteLK2TXARPT16Ip";

    private const string CabacMainAvcC =
        "AU1ACv/hABZnTUAK9ktgIgAAAwACAAADAAweJEpwAQAEaO4PyA==";

    private const string CabacMainIdrNal =
        "ZYiEA6/8G5DK0tdsy79UEFKyNF4ZhtbWUuG1qSwyAkd5wAJHByfXSdHv+hfWfQMJho1ymU9rW5haiOU5hKsh1z+i1YS1KYYP1vf0XMcRwf0vFvkuOpLN4Q2sePnUfKrvugAyZIIG/XUlaVPW0JuDzVLxxIS/8f9BeuwiIyUmXG+RrGV9MimpFIQgLuYQ4BSyPpOILmoCovdoYB3aefol81Yjz6063fS1/FkBaA31ag/iOKmi+LtwVkKAshDjoYbVrKg4xn14RYnSady7ry/ABDKeh5T8Knewng2mDrvJqTEYCmMzWgRSfmW35k0utHo3h/yRX5ItdVGeL5nWC3QwkMuvG2TuDxr8xiSsvSIsei7V3D7fkc5mGK90e2WOjeXKix7/u6gXzgIvKSLCcmjocoQpR6qQ1eAHXUo9owjYRMUaY+rM+fm0r1XJrsD8erNY0HzqcT2Py5yl4LDR+nBgRHt3LGe701UNZQ0fU5XIXNcmBK5LLwdbp0hJL+XFE404NpSWdhfqQQFb5WybNblaUewfZcSC2tx/LJR4iswmIXm1aWMQRandvURUAaSPY6sRiHvzqAG8DFH6vdRf3UgdWPMtQpxQC9NbFOAoWgD//P2miQD1doT8gyBsARJgxWhQDgIarw8efW09cZF4W2mF4rsywzojfLphAjK6bkggrfXXqeyPStWUqJ0WVCVj0VP4C4b82v1j+KDjHkpZ2SiTGCEQ4iAHyupeuYszmSoqpMgsTDrvwU8=";

    private const string CabacMainPNal =
        "QZooLX/TJ7afUEMuL098ZK7c+y0W0ygw7kdvqMxciyw74LXTJGoKF2aR8zeVzlsDCr22Y6f3//oWRtwPFdccongjTFSwgppG1/xDzwfDy2zXZeR2aWGsv7XZBsF4/6kVPTLY7lU2nAtOpqY7+URbbA==";

    private const string CabacMainBNal =
        "AZ5FFL/oRXHyIuHhkp+WVl47U0KUPZHymPYjKU7cUfX7uZaHlXl8FZtyc/gT56gfaKdY9qXg+3j7xNU+Jvs=";

    private static string ComputeFrameHash(H264Mp4DecodedVideoFrame frame)
    {
        byte[] samples = frame.Surface.CopyLumaPlane()
            .Concat(frame.Surface.CopyCbPlane())
            .Concat(frame.Surface.CopyCrPlane())
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(samples)).ToLowerInvariant();
    }

    private static Mp4EncodedSample CreateCabacEncodedSample(
        uint sampleNumber,
        ulong decodeTimeTicks,
        long presentationTimeTicks,
        bool isSyncSample,
        string nalBase64,
        int trailingZeroBytes = 0)
    {
        byte[] nal = Convert.FromBase64String(nalBase64);
        if (trailingZeroBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(trailingZeroBytes));
        if (trailingZeroBytes != 0)
        {
            List<byte> paddedNal = [.. nal];
            int zeroRun = 0;
            for (int i = 0; i < trailingZeroBytes; i++)
            {
                if (zeroRun == 2)
                {
                    paddedNal.Add(0x03);
                    zeroRun = 0;
                }
                paddedNal.Add(0);
                zeroRun++;
            }
            nal = paddedNal.ToArray();
        }

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)nal.Length));
        List<byte> sampleData = [.. length.ToArray()];
        sampleData.AddRange(nal);
        Mp4SampleLocation location = new(
            sampleNumber, 1, 0, (uint)sampleData.Count,
            decodeTimeTicks, 1, isSyncSample)
        {
            SampleDescriptionIndex = 1,
            CompositionOffsetTicks = checked((int)(
                presentationTimeTicks - checked((long)decodeTimeTicks)))
        };
        return new Mp4EncodedSample(location, sampleData.ToArray());
    }

    private static H264Mp4VideoSample CreateCabacSample(
        H264AvcDecoderConfiguration configuration,
        uint sampleNumber,
        ulong decodeTimeTicks,
        long presentationTimeTicks,
        bool isSyncSample,
        string nalBase64,
        int trailingZeroBytes = 0)
    {
        byte[] nalBytes = Convert.FromBase64String(nalBase64);
        if (trailingZeroBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(trailingZeroBytes));
        if (trailingZeroBytes != 0)
        {
            List<byte> paddedNal = [.. nalBytes];
            int zeroRun = 0;
            for (int i = 0; i < trailingZeroBytes; i++)
            {
                if (zeroRun == 2)
                {
                    paddedNal.Add(0x03);
                    zeroRun = 0;
                }
                paddedNal.Add(0);
                zeroRun++;
            }
            nalBytes = paddedNal.ToArray();
        }

        byte[] lengthPrefixed = new byte[nalBytes.Length + 4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            lengthPrefixed, checked((uint)nalBytes.Length));
        Buffer.BlockCopy(nalBytes, 0, lengthPrefixed, 4, nalBytes.Length);
        IReadOnlyList<H264NalUnit> nalUnits =
            new H264NalUnitReader().ReadLengthPrefixed(lengthPrefixed, 4);

        return new H264Mp4VideoSample(
            9, sampleNumber, decodeTimeTicks, presentationTimeTicks,
            1, 3, isSyncSample, configuration, nalUnits);
    }

    [Fact]
    public void ReadCabacMp4Samples_ShouldDecodeIAndPSkipAndBDirectThroughAccessUnitPipeline()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp(maxNumRefFrames: 2));
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp(entropyCodingModeFlag: true));
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true,
            BuildNal(0x65, CreateCabacIdrPcmSliceRbsp(80, 96, 160)));
        H264Mp4VideoSample futurePSample = ReadSample(
            track, 2, 40, false,
            BuildNal(0x41, CreateCabacPSkipSliceRbsp(frameNum: 1, pocLsb: 4)),
            presentationTimeTicks: 80);
        H264Mp4VideoSample bSample = ReadSample(
            track, 3, 80, false,
            BuildNal(0x41, CreateCabacBSkipSliceRbsp(frameNum: 2, pocLsb: 2)),
            presentationTimeTicks: 40);

        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        Queue<IH264CabacSliceDataReader> cabacReaders = new(
            new IH264CabacSliceDataReader[]
            {
                new ScriptedCabacSliceDataReader([1], [1, 1]), // I_PCM mb_type and end-of-slice
                new ScriptedCabacSliceDataReader([1], [1]),    // P_Skip and end-of-slice
                new ScriptedCabacSliceDataReader([1], [1])     // B_Skip and end-of-slice
            });
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets,
            2,
            (reader, _, _, _) =>
            {
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabacReaders.Dequeue();
            });

        H264Mp4AccessUnitDecodeResult idrResult = decoder.Decode(idrSample)!;
        Assert.Equal(H264SliceType.I, idrResult.SliceHeader.SliceType);
        Assert.True(idrResult.Surface.IsComplete);
        Assert.All(idrResult.Surface.CopyLumaPlane(), value => Assert.Equal((byte)80, value));

        H264Mp4AccessUnitDecodeResult pResult = decoder.Decode(futurePSample)!;
        Assert.Equal(H264SliceType.P, pResult.SliceHeader.SliceType);
        Assert.All(pResult.Surface.CopyLumaPlane(), value => Assert.Equal((byte)80, value));

        H264Mp4AccessUnitDecodeResult bResult = decoder.Decode(bSample)!;
        Assert.Equal(H264SliceType.B, bResult.SliceHeader.SliceType);
        Assert.All(bResult.Surface.CopyLumaPlane(), value => Assert.Equal((byte)80, value));
        Assert.Equal(1u, Assert.Single(bResult.ReadyFrames).SampleNumber);
        Assert.Equal(0, Assert.Single(bResult.ReadyFrames).PictureOrderCount);

        IReadOnlyList<H264Mp4DecodedVideoFrame> remaining = decoder.Flush();
        Assert.Equal(new[] { 3u, 2u }, remaining.Select(frame => frame.SampleNumber));
        Assert.Equal(new[] { 2, 4 }, remaining.Select(frame => frame.PictureOrderCount));
        Assert.Equal(new long[] { 40, 80 }, remaining.Select(frame => frame.PresentationTimeTicks));
        Assert.Empty(cabacReaders);
        Assert.Equal(2, decoder.DecodedPictureBuffer.Count);
    }

    private static H264Mp4VideoSample ReadSample(
        Mp4Track track,
        uint sampleNumber,
        ulong decodeTimeTicks,
        bool isSync,
        byte[] nal,
        long? presentationTimeTicks = null)
    {
        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, nal);
        Mp4EncodedSample encoded = new(
            new Mp4SampleLocation(
                sampleNumber, 1, 0, (uint)sampleData.Count,
                decodeTimeTicks, 40, isSync)
            {
                SampleDescriptionIndex = 1,
                CompositionOffsetTicks = checked((int)(
                    (presentationTimeTicks ?? checked((long)decodeTimeTicks)) -
                    checked((long)decodeTimeTicks)))
            },
            sampleData.ToArray());
        return new H264Mp4TrackSampleReader().Read(track, encoded);
    }

    private static byte[] CreateCabacIdrPcmSliceRbsp(
        uint luma, uint cb, uint cr)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.AlignWithOneBits(); // cabac_alignment_one_bit
        bits.WriteBits(0, 9); // arithmetic decoder initial offset
        bits.AlignWithZeroes(); // pcm_alignment_zero_bit
        for (int i = 0; i < 256; i++) bits.WriteBits(luma, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cb, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cr, 8);
        return bits.ToRbsp();
    }

    private static byte[] CreateCabacPSkipSliceRbsp(uint frameNum, uint pocLsb)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(frameNum, 4); // frame_num
        bits.WriteBits(pocLsb, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteUe(0); // cabac_init_idc
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9); // arithmetic decoder initial offset
        return bits.ToRbsp();
    }

    private static byte[] CreateCabacBSkipSliceRbsp(uint frameNum, uint pocLsb)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(1); // B slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(frameNum, 4); // frame_num
        bits.WriteBits(pocLsb, 4); // pic_order_cnt_lsb
        bits.WriteFlag(true); // direct_spatial_mv_pred_flag
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l1
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteUe(0); // cabac_init_idc
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9); // arithmetic decoder initial offset
        return bits.ToRbsp();
    }

    private static byte[] CreatePcmPSliceRbsp(
        uint luma, uint cb, uint cr)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(1, 4); // frame_num
        bits.WriteBits(4, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(30); // P-slice I_PCM macroblock
        bits.AlignWithZeroes();
        for (int i = 0; i < 256; i++) bits.WriteBits(luma, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cb, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cr, 8);
        return bits.ToRbsp();
    }

    private static byte[] CreateBDirectSliceRbsp()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(1); // B slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(2, 4); // frame_num
        bits.WriteBits(2, 4); // pic_order_cnt_lsb
        bits.WriteFlag(true); // direct_spatial_mv_pred_flag
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l1
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(0); // B_Direct_16x16
        bits.WriteUe(0); // coded_block_pattern (inter)
        return bits.ToRbsp();
    }

    private static byte[] CreatePSkipSliceRbsp()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(1, 4); // frame_num
        bits.WriteBits(2, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(1); // mb_skip_run: one P_Skip macroblock
        return bits.ToRbsp();
    }

    private static byte[] CreateSpsRbsp(uint picWidthInMbsMinus1 = 0, uint maxNumRefFrames = 1)
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8);
        bits.WriteBits(0, 8);
        bits.WriteBits(30, 8);
        bits.WriteUe(0); // seq_parameter_set_id
        bits.WriteUe(0); // log2_max_frame_num_minus4
        bits.WriteUe(0); // pic_order_cnt_type
        bits.WriteUe(0); // log2_max_pic_order_cnt_lsb_minus4
        bits.WriteUe(maxNumRefFrames); // max_num_ref_frames
        bits.WriteFlag(false); // gaps_in_frame_num_value_allowed_flag
        bits.WriteUe(picWidthInMbsMinus1); // pic_width_in_mbs_minus1
        bits.WriteUe(0); // pic_height_in_map_units_minus1
        bits.WriteFlag(true); // frame_mbs_only_flag
        bits.WriteFlag(true); // direct_8x8_inference_flag
        bits.WriteFlag(false); // frame_cropping_flag
        bits.WriteFlag(false); // vui_parameters_present_flag
        return bits.ToRbsp();
    }

    private static byte[] CreatePpsRbsp(bool entropyCodingModeFlag = false)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteUe(0); // seq_parameter_set_id
        bits.WriteFlag(entropyCodingModeFlag); // entropy_coding_mode_flag
        bits.WriteFlag(false); // bottom_field_pic_order_in_frame_present_flag
        bits.WriteUe(0); // num_slice_groups_minus1
        bits.WriteUe(0); // num_ref_idx_l0_default_active_minus1
        bits.WriteUe(0); // num_ref_idx_l1_default_active_minus1
        bits.WriteFlag(false); // weighted_pred_flag
        bits.WriteBits(0, 2); // weighted_bipred_idc
        bits.WriteSe(0); // pic_init_qp_minus26
        bits.WriteSe(0); // pic_init_qs_minus26
        bits.WriteSe(0); // chroma_qp_index_offset
        bits.WriteFlag(true); // deblocking_filter_control_present_flag
        bits.WriteFlag(false); // constrained_intra_pred_flag
        bits.WriteFlag(false); // redundant_pic_cnt_present_flag
        return bits.ToRbsp();
    }

    private static byte[] CreateIdrPcmSliceRbsp(
        uint firstMacroblock = 0, uint luma = 80, uint cb = 96, uint cr = 160)
    {
        BitWriter bits = new();
        bits.WriteUe(firstMacroblock); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(25); // I_PCM macroblock
        bits.AlignWithZeroes();

        for (int i = 0; i < 256; i++) bits.WriteBits(luma, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cb, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cr, 8);
        return bits.ToRbsp();
    }

    private static byte[] BuildNal(byte header, byte[] rbsp)
    {
        List<byte> nal = [header];
        int zeroCount = 0;
        foreach (byte value in rbsp)
        {
            if (zeroCount >= 2 && value <= 3)
            {
                nal.Add(3);
                zeroCount = 0;
            }
            nal.Add(value);
            zeroCount = value == 0 ? zeroCount + 1 : 0;
        }
        return nal.ToArray();
    }

    private static void AddLengthPrefixedNal(List<byte> destination, byte[] nal)
    {
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nal.Length));
        destination.Add(length[0]);
        destination.Add(length[1]);
        destination.AddRange(nal);
    }

    private sealed class TracingCabacSliceDataReader(
        IH264CabacSliceDataReader inner,
        List<string> trace) : IH264CabacSliceDataReader, IH264CabacDiagnosticTraceSink
    {
        private IH264CabacDiagnosticStateProvider? StateProvider =>
            inner as IH264CabacDiagnosticStateProvider;

        public bool IsTerminated => inner.IsTerminated;

        public void TraceDiagnostic(string marker) => trace.Add($"-- {marker}");

        public int DecodeBin(int contextIndex)
        {
            H264CabacContextModel? before = StateProvider?.GetContextModel(contextIndex);
            int rangeBefore = StateProvider?.ArithmeticRange ?? 0;
            int offsetBefore = StateProvider?.ArithmeticOffset ?? 0;
            int bitPositionBefore = StateProvider?.BitPosition ?? 0;

            int bin = inner.DecodeBin(contextIndex);

            if (StateProvider is { } state)
            {
                H264CabacContextModel after = state.GetContextModel(contextIndex);
                trace.Add(
                    $"cabac decision ctx={contextIndex} bin={bin} " +
                    $"model={before!.Value.State}/{before.Value.MostProbableSymbol}" +
                    $"->{after.State}/{after.MostProbableSymbol} " +
                    $"range={rangeBefore}->{state.ArithmeticRange} " +
                    $"offset={offsetBefore}->{state.ArithmeticOffset} " +
                    $"bitpos={bitPositionBefore}->{state.BitPosition}");
            }
            else
            {
                trace.Add($"ctx[{contextIndex}]={bin}");
            }

            return bin;
        }

        public int DecodeBypass()
        {
            int rangeBefore = StateProvider?.ArithmeticRange ?? 0;
            int offsetBefore = StateProvider?.ArithmeticOffset ?? 0;
            int bitPositionBefore = StateProvider?.BitPosition ?? 0;
            int bin = inner.DecodeBypass();

            if (StateProvider is { } state)
                trace.Add(
                    $"cabac bypass bin={bin} range={rangeBefore}->{state.ArithmeticRange} " +
                    $"offset={offsetBefore}->{state.ArithmeticOffset} " +
                    $"bitpos={bitPositionBefore}->{state.BitPosition}");
            else
                trace.Add($"bypass={bin}");

            return bin;
        }

        public int DecodeTerminate()
        {
            int rangeBefore = StateProvider?.ArithmeticRange ?? 0;
            int offsetBefore = StateProvider?.ArithmeticOffset ?? 0;
            int bitPositionBefore = StateProvider?.BitPosition ?? 0;
            int bin = inner.DecodeTerminate();

            if (StateProvider is { } state)
                trace.Add(
                    $"cabac terminate bin={bin} range={rangeBefore}->{state.ArithmeticRange} " +
                    $"offset={offsetBefore}->{state.ArithmeticOffset} " +
                    $"bitpos={bitPositionBefore}->{state.BitPosition}");
            else
                trace.Add($"terminate={bin}");

            return bin;
        }

        public void MarkTerminated()
        {
            trace.Add("marked-terminated");
            inner.MarkTerminated();
        }

        public void RestartAfterPcm()
        {
            trace.Add("pcm-restart");
            inner.RestartAfterPcm();
            if (StateProvider is { } state)
                trace.Add(
                    $"cabac-restart range={state.ArithmeticRange} " +
                    $"offset={state.ArithmeticOffset} bitpos={state.BitPosition}");
        }
    }

    private sealed class ScriptedCabacSliceDataReader : IH264CabacSliceDataReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _terminateBins;

        public ScriptedCabacSliceDataReader(
            IEnumerable<int> decisionBins,
            IEnumerable<int> terminateBins)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _terminateBins = new Queue<int>(terminateBins);
        }

        public bool IsTerminated { get; private set; }

        public int DecodeBin(int contextIndex) => _decisionBins.Dequeue();
        public int DecodeBypass() =>
            throw new InvalidOperationException("These MP4 integration fixtures do not use bypass bins.");
        public int DecodeTerminate() => _terminateBins.Dequeue();
        public void MarkTerminated() => IsTerminated = true;
        public void RestartAfterPcm() { }
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteFlag(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public void WriteUe(uint value)
        {
            uint code = value + 1;
            int leadingZeroes = 0;
            for (uint current = code; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            WriteBits(code, leadingZeroes + 1);
        }

        public void WriteSe(int value) => WriteUe(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));

        public void AlignWithOneBits()
        {
            while (_bits.Count % 8 != 0)
                _bits.Add(true);
        }

        public void AlignWithZeroes()
        {
            while (_bits.Count % 8 != 0)
                _bits.Add(false);
        }

        public byte[] ToRbsp()
        {
            List<bool> bits = [.. _bits, true];
            while (bits.Count % 8 != 0)
                bits.Add(false);
            byte[] result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i])
                    result[i / 8] |= (byte)(1 << (7 - i % 8));
            return result;
        }
    }
}
