namespace XenMotion.Core.Models.Catalog;

/// <summary>Streaming download plus metadata needed for resumable downloads.</summary>
public sealed class CatalogDownloadStream : IAsyncDisposable, IDisposable
{
    public CatalogDownloadStream(Stream content, long? contentLength, bool isPartial, string? entityTag = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentLength = contentLength;
        IsPartial = isPartial;
        EntityTag = entityTag;
    }

    public Stream Content { get; }
    public long? ContentLength { get; }
    public bool IsPartial { get; }
    public string? EntityTag { get; }

    public void Dispose() => Content.Dispose();
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
