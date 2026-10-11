using XenMotion.Core.Models.Catalog;

namespace XenMotion.Core.Interfaces;

/// <summary>Optional catalog capability for HTTP range/resume downloads.</summary>
public interface IResumableCatalogDownloadProvider
{
    Task<CatalogDownloadStream> OpenDownloadStreamAsync(
        CatalogItem item,
        long offset,
        CancellationToken ct = default,
        string? entityTag = null);
}
