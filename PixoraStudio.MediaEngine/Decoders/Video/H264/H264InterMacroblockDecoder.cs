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

        return _motionVectorReconstructor.ReconstructSkipped(
            pictureState, macroblockX, macroblockY, sliceId);
    }
}
