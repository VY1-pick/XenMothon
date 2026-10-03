using Lumen.Core.Models.Creator;

namespace Lumen.Core.Interfaces;

public interface ICreatorService
{
    const long MaxUploadBytes = 1_500_000_000;

    Task<CreatorProfile?> GetProfileAsync(CancellationToken ct = default);
    Task<CreatorSubmission> UploadAsync(
        CreatorSubmission submission,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
