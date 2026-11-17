using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Locates the optional edit list inside a track box.
/// </summary>
public sealed class Mp4EditListLocator
{
    public Mp4Box? Find(Mp4Box trackBox)
    {
        ArgumentNullException.ThrowIfNull(trackBox);

        Mp4Box? editContainer = trackBox.Children
            .SingleOrDefault(box => box.Type == "edts");

        if (editContainer is null)
        {
            return null;
        }

        Mp4Box[] editLists = editContainer.Children
            .Where(box => box.Type == "elst")
            .ToArray();

        if (editLists.Length > 1)
        {
            throw new Mp4ParsingException(
                "The track contains multiple edit lists.");
        }

        return editLists.SingleOrDefault();
    }
}