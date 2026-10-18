namespace XenMotion.Core.Enums;

/// <summary>Where Explore loads catalog metadata and media from.</summary>
public enum CatalogSource
{
    /// <summary>Offline built-in catalog. Always available and the default.</summary>
    Local = 0,

    /// <summary>Remote catalog API (the six <c>api/v1/catalog/*</c> routes served by XenMotion.Server).</summary>
    ServerApi = 1,

    /// <summary>
    /// A single static <c>catalog.json</c> plus relative media, served by any HTTPS host or CDN.
    /// This is the shape <c>XenMotion.Server --static</c> and the local demo catalog both produce.
    /// </summary>
    StaticManifest = 2
}
