namespace XenMotion.Application.Services;

/// <summary>
/// Contract for enforcing a single active application instance and handling activation signals.
/// </summary>
public interface ISingleInstanceService : IDisposable
{
    bool IsFirstInstance { get; }
    bool TryAcquireOwnership();
    void SignalExistingInstance();

    event EventHandler? SecondInstanceLaunched;
}
