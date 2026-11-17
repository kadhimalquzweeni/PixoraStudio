using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Locates media-data regions inside an MP4 box tree.
/// </summary>
public sealed class Mp4MediaDataReader
{
    /// <summary>
    /// Reads all mdat regions from the supplied MP4 box tree.
    /// </summary>
    public IReadOnlyList<Mp4MediaData> Read(Mp4Box root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<Mp4MediaData> mediaDataRegions = [];

        CollectMediaData(root, mediaDataRegions);

        if (mediaDataRegions.Count == 0)
        {
            throw new Mp4ParsingException(
                "The MP4 container does not contain an mdat box.");
        }

        return mediaDataRegions.AsReadOnly();
    }

    private static void CollectMediaData(
        Mp4Box box,
        List<Mp4MediaData> results)
    {
        if (box.Type == "mdat")
        {
            results.Add(
                new Mp4MediaData(
                    box.DataOffset,
                    box.DataSize));

            return;
        }

        foreach (Mp4Box child in box.Children)
        {
            CollectMediaData(child, results);
        }
    }
}
