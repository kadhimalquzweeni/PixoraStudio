
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Locates the sample table box within an MP4 track.
/// </summary>
public sealed class Mp4SampleTableLocator
{
    public Mp4Box Locate(Mp4Box trackBox)
    {
        ArgumentNullException.ThrowIfNull(trackBox);

        if (trackBox.Type != "trak")
        {
            throw new Mp4ParsingException(
                "The supplied box is not an MP4 track box.");
        }

        Mp4Box mediaBox = GetRequiredChild(trackBox, "mdia");

        Mp4Box mediaInformationBox =
            GetRequiredChild(mediaBox, "minf");

        return GetRequiredChild(mediaInformationBox, "stbl");
    }

    private static Mp4Box GetRequiredChild(
        Mp4Box parent,
        string childType)
    {
        Mp4Box[] matches = parent.Children
            .Where(child => child.Type == childType)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new Mp4ParsingException(
                $"The '{parent.Type}' box does not contain a '{childType}' box.");
        }

        if (matches.Length > 1)
        {
            throw new Mp4ParsingException(
                $"The '{parent.Type}' box contains multiple '{childType}' boxes.");
        }

        return matches[0];
    }
}
