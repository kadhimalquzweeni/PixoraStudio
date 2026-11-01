namespace PixoraStudio.Core.Models;

public class MediaFile
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName =>
        Path.GetFileName(FilePath);

    public MediaType MediaType { get; set; }
}
