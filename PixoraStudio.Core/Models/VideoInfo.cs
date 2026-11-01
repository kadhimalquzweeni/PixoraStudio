
namespace PixoraStudio.Core.Models
{
    public class VideoInfo
    {
        public string FilePath { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public TimeSpan Duration { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public double FramesPerSecond { get; set; }
    }
}
