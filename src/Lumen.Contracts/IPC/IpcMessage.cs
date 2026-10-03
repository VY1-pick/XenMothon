namespace Lumen.Contracts.IPC;

/// <summary>
/// Base class for all inter-process communication messages between UI Host and Render Host.
/// </summary>
public abstract record IpcMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public string MessageType => GetType().Name;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
