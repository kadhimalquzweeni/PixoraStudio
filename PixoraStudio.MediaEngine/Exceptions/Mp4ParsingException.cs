
namespace PixoraStudio.MediaEngine.Exceptions;

public sealed class Mp4ParsingException : Exception
{
    public Mp4ParsingException(string message)
        : base(message)
    {
    }

    public Mp4ParsingException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
