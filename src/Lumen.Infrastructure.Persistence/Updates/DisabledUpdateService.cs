using Lumen.Core.Interfaces;
using Lumen.Core.Models.Updates;

namespace Lumen.Infrastructure.Persistence.Updates;

public sealed class DisabledUpdateService : IUpdateService
{
    public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        => Task.FromResult<UpdateInfo?>(null);
}
