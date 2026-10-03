namespace Lumen.Core.Models.Creator;

public sealed record CreatorProfile(
    string Id,
    string DisplayName,
    string? AvatarUrl,
    int PublishedCount,
    int PendingCount);
