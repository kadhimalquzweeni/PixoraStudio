using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads the byte-aligned raw sample payload of an 8-bit frame I_PCM
/// macroblock. Samples are returned in raster order for each component.
/// </summary>
public sealed class H264PcmMacroblockReader
{
    public H264PcmMacroblock Read(
        H264BitReader reader,
        H264MacroblockHeader header,
        H264SequenceParameterSet sps,
        H264PictureParameterSetHeader pps)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        if (!header.IsPcm || !header.IsIntra || header.IsSkipped)
            throw new ArgumentException("The macroblock header must describe an I_PCM macroblock.", nameof(header));
        if (pps.EntropyCodingModeFlag)
            throw new NotSupportedException("CABAC I_PCM parsing is not implemented.");
        if (pps.SequenceParameterSetId != sps.Id)
            throw new ArgumentException("The PPS must reference the supplied SPS.", nameof(pps));
        if (!sps.FrameMbsOnlyFlag || sps.SeparateColourPlaneFlag ||
            sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0 ||
            sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("I_PCM parsing supports frame-only 8-bit non-separate 4:2:0 and 4:2:2 pictures.");

        reader.AlignToByteAndValidateZero();

        byte[] luma = ReadSamples(reader, 256);
        int chromaHeight = sps.ChromaFormatIdc == 1 ? 8 : 16;
        int chromaSamples = checked(8 * chromaHeight);
        byte[] cb = ReadSamples(reader, chromaSamples);
        byte[] cr = ReadSamples(reader, chromaSamples);

        return new H264PcmMacroblock(header.MacroblockAddress, luma, cb, cr);
    }

    private static byte[] ReadSamples(H264BitReader reader, int count)
    {
        byte[] samples = new byte[count];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = checked((byte)reader.ReadBits(8));
        return samples;
    }
}
