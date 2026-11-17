
namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Describes the window sequence used by an AAC individual channel stream.
/// </summary>
public enum AacWindowSequence
{
    OnlyLongSequence = 0,
    LongStartSequence = 1,
    EightShortSequence = 2,
    LongStopSequence = 3
}
