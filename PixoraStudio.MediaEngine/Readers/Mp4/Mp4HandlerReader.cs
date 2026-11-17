
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads the handler type from an MP4 hdlr box.
/// </summary>
public sealed class Mp4HandlerReader
{
    public Mp4HandlerType Read(Stream stream, Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "hdlr")
        {
            throw new Mp4ParsingException(
                "Expected an hdlr box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The hdlr box extends beyond the stream.");
            }

            // FullBox header (4 bytes) + pre_defined (4 bytes)
            // + handler_type (4 bytes).
            if (box.DataSize < 12)
            {
                throw new Mp4ParsingException(
                    "The hdlr box is too small.");
            }

            byte[] data = new byte[12];

            stream.Position = box.DataOffset;
            stream.ReadExactly(data);

            string handler = Encoding.ASCII.GetString(
                data,
                8,
                4);

            return handler switch
            {
                "vide" => Mp4HandlerType.Video,
                "soun" => Mp4HandlerType.Audio,
                "text" => Mp4HandlerType.Text,
                "subt" => Mp4HandlerType.Subtitle,
                "meta" => Mp4HandlerType.Metadata,
                _ => Mp4HandlerType.Unknown
            };
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading hdlr.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
