using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Interfaces;

/// <summary>Inspects the graphics adapters and hardware-decoding prerequisites.</summary>
public interface IHardwareVideoCapabilityService
{
    Task<HardwareVideoCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);
}
