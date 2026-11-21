using PixoraStudio.Core.Models;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>Chooses a usable hardware adapter, preferring dedicated video memory.</summary>
public static class GraphicsAdapterSelector
{
    public static GraphicsAdapterCapabilities? SelectPreferred(
        IEnumerable<GraphicsAdapterCapabilities> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        return adapters
            .Where(adapter =>
                !adapter.IsSoftwareAdapter && adapter.CanCreateD3D11Device)
            .OrderByDescending(adapter => adapter.DedicatedVideoMemoryBytes)
            .ThenByDescending(adapter => adapter.SharedSystemMemoryBytes)
            .ThenBy(adapter => adapter.AdapterIndex)
            .FirstOrDefault();
    }
}
