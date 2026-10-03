using Lumen.Core.Models.Catalog;

namespace Lumen.Core.Interfaces;

/// <summary>Optional catalog capability for HTTP range/resume downloads.</summary>
public interface IResumableCatalogDownloadProvider
{
    Task<CatalogDownloadStream> OpenDownloadStreamAsync(
        CatalogItem item,
        long offset,
        CancellationToken ct = default);
}
