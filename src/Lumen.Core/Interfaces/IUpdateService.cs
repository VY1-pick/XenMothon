using Lumen.Core.Models.Updates;

namespace Lumen.Core.Interfaces;

/// <summary>
/// Optional update-check abstraction. It is deliberately separated from installation so a future
/// signed installer/updater can be introduced without coupling the application to a transport.
/// </summary>
public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
}
