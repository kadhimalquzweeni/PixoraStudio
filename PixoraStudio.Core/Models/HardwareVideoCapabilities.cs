namespace PixoraStudio.Core.Models;

/// <summary>
/// Describes the graphics and Media Foundation facilities available to
/// PixoraStudio. This indicates whether hardware decoding can be attempted;
/// codec/profile support is confirmed when Media Foundation opens a video.
/// </summary>
public sealed record HardwareVideoCapabilities(
    bool IsWindowsPlatform,
    bool IsMediaFoundationAvailable,
    IReadOnlyList<GraphicsAdapterCapabilities> Adapters,
    GraphicsAdapterCapabilities? SelectedAdapter,
    string StatusMessage)
{
    public bool CanAttemptHardwareDecoding =>
        IsWindowsPlatform &&
        IsMediaFoundationAvailable &&
        SelectedAdapter is { CanCreateD3D11Device: true, IsSoftwareAdapter: false };
}
