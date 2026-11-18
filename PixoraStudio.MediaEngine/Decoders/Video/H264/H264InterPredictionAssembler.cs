using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Places list-0 motion-compensated sub-partition buffers into a complete
/// 16x16 macroblock prediction surface for residual reconstruction.
/// </summary>
public static class H264InterPredictionAssembler
{
    public static H264MotionCompensatedBlock Assemble(
        IReadOnlyList<H264InterMacroblockPrediction> predictions,
        uint chromaFormatIdc)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        if (chromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Inter prediction assembly supports 4:2:0 and 4:2:2 only.");
        if (predictions.Count == 0)
            throw new ArgumentException("At least one inter partition prediction is required.", nameof(predictions));

        int chromaHeight = chromaFormatIdc == 1 ? 8 : 16;
        byte[] luma = new byte[256];
        byte[] cb = new byte[8 * chromaHeight];
        byte[] cr = new byte[8 * chromaHeight];
        bool[] lumaWritten = new bool[256];
        bool[] chromaWritten = new bool[8 * chromaHeight];

        foreach (H264InterMacroblockPrediction item in predictions)
        {
            if (item is null || item.Partition is null || item.Prediction is null)
                throw new ArgumentException("Partition predictions cannot contain null entries.", nameof(predictions));

            H264ReconstructedInterPartition partition = item.Partition;
            H264MotionCompensatedBlock block = item.Prediction;
            if (partition.X < 0 || partition.Y < 0 ||
                partition.Width <= 0 || partition.Height <= 0 ||
                partition.X + partition.Width > 4 ||
                partition.Y + partition.Height > 4)
                throw new FormatException("An inter partition lies outside the 16x16 macroblock.");

            int expectedLumaWidth = checked(partition.Width * 4);
            int expectedLumaHeight = checked(partition.Height * 4);
            int chromaScaleY = chromaFormatIdc == 1 ? 2 : 4;
            int expectedChromaWidth = checked(partition.Width * 2);
            int expectedChromaHeight = checked(partition.Height * chromaScaleY);
            if (block.LumaWidth != expectedLumaWidth ||
                block.LumaHeight != expectedLumaHeight ||
                block.Luma.Length != expectedLumaWidth * expectedLumaHeight ||
                block.ChromaWidth != expectedChromaWidth ||
                block.ChromaHeight != expectedChromaHeight ||
                block.Cb.Length != expectedChromaWidth * expectedChromaHeight ||
                block.Cr.Length != expectedChromaWidth * expectedChromaHeight)
                throw new FormatException("A partition prediction's plane dimensions do not match its macroblock geometry.");

            int lumaOriginX = partition.X * 4;
            int lumaOriginY = partition.Y * 4;
            for (int y = 0; y < expectedLumaHeight; y++)
            for (int x = 0; x < expectedLumaWidth; x++)
            {
                int destination = (lumaOriginY + y) * 16 + lumaOriginX + x;
                if (lumaWritten[destination])
                    throw new FormatException("Inter partition predictions overlap in the luma plane.");
                lumaWritten[destination] = true;
                luma[destination] = block.Luma[y * expectedLumaWidth + x];
            }

            int chromaOriginX = partition.X * 2;
            int chromaOriginY = partition.Y * chromaScaleY;
            for (int y = 0; y < expectedChromaHeight; y++)
            for (int x = 0; x < expectedChromaWidth; x++)
            {
                int destination = (chromaOriginY + y) * 8 + chromaOriginX + x;
                if (chromaWritten[destination])
                    throw new FormatException("Inter partition predictions overlap in a chroma plane.");
                chromaWritten[destination] = true;
                cb[destination] = block.Cb[y * expectedChromaWidth + x];
                cr[destination] = block.Cr[y * expectedChromaWidth + x];
            }
        }

        if (lumaWritten.Any(value => !value) || chromaWritten.Any(value => !value))
            throw new FormatException("Inter partition predictions do not completely cover the macroblock.");

        return new H264MotionCompensatedBlock(16, 16, 8, chromaHeight, luma, cb, cr);
    }
}
