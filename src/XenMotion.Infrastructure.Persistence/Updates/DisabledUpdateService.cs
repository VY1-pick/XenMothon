using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Updates;

namespace XenMotion.Infrastructure.Persistence.Updates;

public sealed class DisabledUpdateService : IUpdateService
{
    public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        => Task.FromResult<UpdateInfo?>(null);
}
