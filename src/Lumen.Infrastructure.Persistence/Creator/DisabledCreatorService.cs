using Lumen.Core.Interfaces;
using Lumen.Core.Models.Creator;

namespace Lumen.Infrastructure.Persistence.Creator;

public sealed class DisabledCreatorService : ICreatorService
{
    public Task<CreatorProfile?> GetProfileAsync(CancellationToken ct = default)
        => Task.FromResult<CreatorProfile?>(null);

    public Task<CreatorSubmission> UploadAsync(CreatorSubmission submission, IProgress<double>? progress = null, CancellationToken ct = default)
        => throw new InvalidOperationException("Creator uploads are unavailable until CreatorBackendUrl is configured.");
}
