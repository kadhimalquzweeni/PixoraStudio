namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Converts EBSP NAL payloads into raw byte sequence payloads.</summary>
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

    /// <summary>
    /// Validates EBSP emulation-prevention syntax. Framing readers call this
    /// even for NAL types whose RBSP is not otherwise consumed by the decoder.
    /// </summary>
    public static void ValidateEbsp(ReadOnlySpan<byte> ebsp)
    {
        int zeroCount = 0;
        for (int i = 0; i < ebsp.Length; i++)
        {
            byte value = ebsp[i];
            if (zeroCount >= 2)
            {
                if (value == 0x03)
                {
                    if (i + 1 >= ebsp.Length || ebsp[i + 1] > 0x03)
                        throw new FormatException("Invalid H.264 emulation-prevention byte.");
                    zeroCount = 0;
                    continue;
                }

                if (value <= 0x02)
                    throw new FormatException(
                        "The EBSP contains an unescaped start-code emulation sequence.");
            }

            zeroCount = value == 0 ? zeroCount + 1 : 0;
        }
    }
}
