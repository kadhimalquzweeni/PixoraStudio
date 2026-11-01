using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Interfaces;

public interface IMediaTypeDetector
{
    MediaType Detect(string filePath);
}
