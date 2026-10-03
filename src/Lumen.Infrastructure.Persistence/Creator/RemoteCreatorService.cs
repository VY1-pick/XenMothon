using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models.Creator;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Persistence.Creator;

/// <summary>HTTP creator client. The server owns moderation; the client only uploads and tracks returned state.</summary>
public sealed class RemoteCreatorService : ICreatorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteCreatorService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public RemoteCreatorService(HttpClient httpClient, ILogger<RemoteCreatorService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CreatorProfile?> GetProfileAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("api/v1/creators/me", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatorProfile>(JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<CreatorSubmission> UploadAsync(CreatorSubmission submission, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidateFile(submission.FilePath, submission.FileSizeBytes);
        ct.ThrowIfCancellationRequested();

        submission.State = CreatorSubmissionState.Uploading;
        submission.Progress = 0;
        submission.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await using var file = new FileStream(submission.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
            using var form = new MultipartFormDataContent();
            AddString(form, "title", submission.Title);
            AddString(form, "description", submission.Description);
            AddString(form, "categoryId", submission.CategoryId);
            AddString(form, "tags", string.Join(',', submission.Tags.Where(x => !string.IsNullOrWhiteSpace(x))));

            var content = new ProgressStreamContent(file, submission.FileSizeBytes, progress, ct);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "file", Path.GetFileName(submission.FilePath));

            using var response = await _httpClient.PostAsync("api/v1/creators/me/submissions", form, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<SubmissionResponse>(JsonOptions, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Creator upload response was empty.");

            submission.ServerSubmissionId = dto.Id;
            submission.State = dto.State == CreatorSubmissionState.Draft ? CreatorSubmissionState.PendingModeration : dto.State;
            submission.RejectionReason = dto.RejectionReason;
            submission.Progress = 100;
            submission.UpdatedAtUtc = DateTime.UtcNow;
            progress?.Report(100);
            return submission;
        }
        catch (OperationCanceledException)
        {
            submission.State = CreatorSubmissionState.Cancelled;
            submission.UpdatedAtUtc = DateTime.UtcNow;
            throw;
        }
        catch (Exception ex)
        {
            submission.State = CreatorSubmissionState.Failed;
            submission.UpdatedAtUtc = DateTime.UtcNow;
            _logger.LogError(ex, "Creator upload failed for submission {SubmissionId}.", submission.Id);
            throw;
        }
    }

    private static void ValidateFile(string path, long declaredSize)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Creator upload file was not found.", path);
        var info = new FileInfo(path);
        if (info.Length <= 0) throw new InvalidDataException("Creator upload file is empty.");
        if (info.Length > ICreatorService.MaxUploadBytes)
            throw new InvalidOperationException("Creator uploads are limited to 1.5 GB.");
        if (declaredSize > 0 && declaredSize != info.Length)
            throw new InvalidDataException("Creator submission file size does not match the file on disk.");
        var ext = Path.GetExtension(path);
        if (!new[] { ".mp4", ".m4v", ".webm", ".wmv", ".asf", ".avi", ".mov", ".jpg", ".jpeg", ".png", ".bmp", ".webp" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Creator upload format '{ext}' is not supported.");
    }

    private static void AddString(MultipartFormDataContent form, string name, string value)
        => form.Add(new StringContent(value ?? string.Empty), name);

    private sealed class SubmissionResponse
    {
        public string? Id { get; set; }
        public CreatorSubmissionState State { get; set; }
        public string? RejectionReason { get; set; }
    }

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly long _length;
        private readonly IProgress<double>? _progress;
        private readonly CancellationToken _ct;

        public ProgressStreamContent(Stream stream, long length, IProgress<double>? progress, CancellationToken ct)
        {
            _stream = stream; _length = length; _progress = progress; _ct = ct;
        }
        protected override bool TryComputeLength(out long length) { length = _length; return true; }
        protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
        {
            byte[] buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = await _stream.ReadAsync(buffer.AsMemory(), _ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), _ct).ConfigureAwait(false);
                total += read;
                _progress?.Report(_length == 0 ? 0 : total * 100d / _length);
            }
        }
    }
}
