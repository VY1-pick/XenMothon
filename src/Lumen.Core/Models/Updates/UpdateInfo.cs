namespace Lumen.Core.Models.Updates;

/// <summary>
/// Describes a release published by the update service. The client never installs an update itself;
/// it only reports validated metadata and lets the release channel decide how installation is performed.
/// </summary>
public sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string? ReleaseNotesUrl,
    DateTimeOffset? PublishedAtUtc,
    bool IsMandatory = false);
