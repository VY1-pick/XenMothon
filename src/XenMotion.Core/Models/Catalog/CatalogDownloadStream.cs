namespace XenMotion.Core.Models.Catalog;

/// <summary>Streaming download plus metadata needed for resumable downloads.</summary>
public sealed class CatalogDownloadStream : IAsyncDisposable, IDisposable
{
    public CatalogDownloadStream(Stream content, long? contentLength, bool isPartial, string? entityTag = null, string? contentType = null, long? totalLength = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentLength = contentLength;
        IsPartial = isPartial;
        EntityTag = entityTag;
        ContentType = contentType;
        TotalLength = totalLength ?? (isPartial ? null : contentLength);
    }

    public Stream Content { get; }
    public long? ContentLength { get; }
    public bool IsPartial { get; }
    public string? EntityTag { get; }

    /// <summary>Media type reported by the transport's response, when it knows one.</summary>
    public string? ContentType { get; }
    public long? TotalLength { get; }

    public void Dispose() => Content.Dispose();
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
