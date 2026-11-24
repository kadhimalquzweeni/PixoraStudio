using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Integrates CAVLC P inter macroblock syntax parsing with list-0 motion-vector
/// prediction and picture-relative state updates.
/// </summary>
public sealed class H264InterMacroblockDecoder
{
    private readonly H264InterMacroblockSyntaxReader _syntaxReader;
    private readonly H264InterMacroblockMotionVectorReconstructor _motionVectorReconstructor;

    public H264InterMacroblockDecoder()
        : this(new H264InterMacroblockSyntaxReader(),
            new H264InterMacroblockMotionVectorReconstructor())
    {
    }

    public H264InterMacroblockDecoder(
        H264InterMacroblockSyntaxReader syntaxReader,
        H264InterMacroblockMotionVectorReconstructor motionVectorReconstructor)
    {
        ArgumentNullException.ThrowIfNull(syntaxReader);
        ArgumentNullException.ThrowIfNull(motionVectorReconstructor);
        _syntaxReader = syntaxReader;
        _motionVectorReconstructor = motionVectorReconstructor;
    }

    /// <summary>
    /// Parses and reconstructs one non-skipped P inter macroblock. The
    /// active-reference argument follows slice syntax and is
    /// num_ref_idx_l0_active_minus1; the reconstructor receives the resulting
    /// active list count.
    /// </summary>
    public H264InterMacroblockDecodeResult Decode(
        H264BitReader reader,
        H264MacroblockHeader macroblockHeader,
        uint numRefIdxL0ActiveMinus1,
        H264MotionVectorPictureState pictureState,
        int macroblockX,
        int macroblockY,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(pictureState);
        if (macroblockHeader.IsSkipped || macroblockHeader.IsIntra || macroblockHeader.IsPcm)
            throw new ArgumentException(
                "The macroblock header must describe a non-skipped inter P macroblock.",
                nameof(macroblockHeader));
        if (numRefIdxL0ActiveMinus1 > 31)
            throw new ArgumentOutOfRangeException(nameof(numRefIdxL0ActiveMinus1));

        H264InterMacroblockSyntax syntax = _syntaxReader.Read(
            reader, macroblockHeader, numRefIdxL0ActiveMinus1);
        IReadOnlyList<H264ReconstructedInterPartition> partitions =
            _motionVectorReconstructor.Reconstruct(
                syntax, pictureState, macroblockX, macroblockY, sliceId,
                checked((int)(numRefIdxL0ActiveMinus1 + 1)));

        return new H264InterMacroblockDecodeResult(syntax, partitions);
    }

    /// <summary>
    /// Expands the mb_skip_run returned by the macroblock header reader,
    /// deriving and recording each skipped macroblock in raster order.
    /// </summary>
    public IReadOnlyList<H264ReconstructedInterPartition> DecodeSkippedRun(
        H264MacroblockHeader macroblockHeader,
        H264MotionVectorPictureState pictureState,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(pictureState);
        if (!macroblockHeader.IsSkipped || macroblockHeader.SkippedMacroblockCount == 0)
            throw new ArgumentException(
                "A skipped macroblock header with a non-zero skip run is required.",
                nameof(macroblockHeader));
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));

        uint pictureMacroblockCount = checked(
            (uint)pictureState.MacroblockWidth * (uint)pictureState.MacroblockHeight);
        if (macroblockHeader.MacroblockAddress >= pictureMacroblockCount ||
            macroblockHeader.SkippedMacroblockCount >
                pictureMacroblockCount - macroblockHeader.MacroblockAddress)
            throw new FormatException("mb_skip_run exceeds the remaining picture macroblocks.");

        List<H264ReconstructedInterPartition> result =
            new(checked((int)macroblockHeader.SkippedMacroblockCount));
        for (uint offset = 0; offset < macroblockHeader.SkippedMacroblockCount; offset++)
        {
            uint address = macroblockHeader.MacroblockAddress + offset;
            int macroblockX = checked((int)(address % (uint)pictureState.MacroblockWidth));
            int macroblockY = checked((int)(address / (uint)pictureState.MacroblockWidth));
            result.Add(_motionVectorReconstructor.ReconstructSkipped(
                pictureState, macroblockX, macroblockY, sliceId));
        }
        return result;
    }

    /// <summary>Derives and records the list-0 vector for one P_Skip macroblock.</summary>
    public H264ReconstructedInterPartition DecodeSkipped(
        H264MacroblockHeader macroblockHeader,
        H264MotionVectorPictureState pictureState,
        int macroblockX,
        int macroblockY,
        int sliceId)
    {
        ArgumentNullException.ThrowIfNull(macroblockHeader);
        ArgumentNullException.ThrowIfNull(pictureState);
        if (!macroblockHeader.IsSkipped || macroblockHeader.SkippedMacroblockCount != 1)
            throw new ArgumentException(
                "A skipped macroblock header with a skip count of one is required.",
                nameof(macroblockHeader));
        if (macroblockX < 0 || macroblockX >= pictureState.MacroblockWidth ||
            macroblockY < 0 || macroblockY >= pictureState.MacroblockHeight ||
            macroblockHeader.MacroblockAddress !=
                (uint)(macroblockY * pictureState.MacroblockWidth + macroblockX))
            throw new ArgumentException(
                "The skipped macroblock coordinates do not match its parsed macroblock address.",
                nameof(macroblockHeader));

        return _motionVectorReconstructor.ReconstructSkipped(
            pictureState, macroblockX, macroblockY, sliceId);
    }
}
