namespace XenMotion.Core.Enums;

/// <summary>
/// Subscription and local download state for a catalog item.
/// </summary>
public enum CatalogDownloadState
{
    NotDownloaded = 0,
    Downloading = 1,
    Downloaded = 2,
    Failed = 3
}
