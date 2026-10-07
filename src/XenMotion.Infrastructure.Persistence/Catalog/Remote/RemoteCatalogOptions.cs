namespace XenMotion.Infrastructure.Persistence.Catalog.Remote;

/// <summary>
/// Runtime configuration for the optional remote catalog.
/// The client remains offline-first when UseRemoteCatalog is false or the endpoint is unavailable.
/// </summary>
public sealed record RemoteCatalogOptions
{
    public bool UseRemoteCatalog { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public bool UseLocalFallback { get; init; } = true;
}
