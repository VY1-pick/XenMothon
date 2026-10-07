using XenMotion.Core.Enums;

namespace XenMotion.Core.Models.Creator;

/// <summary>Local representation of a creator upload/submission.</summary>
public sealed class CreatorSubmission
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public string CategoryId { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public CreatorSubmissionState State { get; set; } = CreatorSubmissionState.Draft;
    public double Progress { get; set; }
    public long FileSizeBytes { get; init; }
    public string? ServerSubmissionId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
