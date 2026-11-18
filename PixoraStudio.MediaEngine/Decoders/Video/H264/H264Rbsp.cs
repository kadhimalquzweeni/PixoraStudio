namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Converts an EBSP NAL payload into its raw byte sequence payload.</summary>
public static class H264Rbsp
{
    public static byte[] FromEbsp(ReadOnlySpan<byte> ebsp)
    {
        if (ebsp.IsEmpty) return [];

        byte[] result = new byte[ebsp.Length];
        int written = 0;
        int zeroCount = 0;

        for (int i = 0; i < ebsp.Length; i++)
        {
            byte value = ebsp[i];
            if (zeroCount >= 2 && value == 0x03)
            {
                if (i + 1 >= ebsp.Length || ebsp[i + 1] > 0x03)
                    throw new FormatException("Invalid H.264 emulation-prevention byte.");
                zeroCount = 0;
                continue;
            }

            result[written++] = value;
            zeroCount = value == 0 ? zeroCount + 1 : 0;
        }

        return result.AsSpan(0, written).ToArray();
    }
}
