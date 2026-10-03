using System.Net;
using System.Text;
using Lumen.Core.Enums;
using Lumen.Core.Models.Creator;
using Lumen.Infrastructure.Persistence.Creator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lumen.Core.Tests.Catalog;

public sealed class CreatorServiceTests
{
    [Fact]
    public async Task Upload_StreamsMultipartFile_AndMapsPendingState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lumen-creator-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, "tiny-media", Encoding.UTF8);
        try
        {
            using var client = new HttpClient(new RecordingHandler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                var body = await request.Content.ReadAsStringAsync();
                Assert.Contains("Creator Test", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"submission-1\",\"state\":\"PendingModeration\"}", Encoding.UTF8, "application/json")
                };
            }))
            {
                BaseAddress = new Uri("https://creator.test/")
            };
            var service = new RemoteCreatorService(client, NullLogger<RemoteCreatorService>.Instance);
            var submission = new CreatorSubmission
            {
                FilePath = path,
                FileSizeBytes = new FileInfo(path).Length,
                Title = "Creator Test"
            };

            var result = await service.UploadAsync(submission);

            Assert.Equal("submission-1", result.ServerSubmissionId);
            Assert.Equal(CreatorSubmissionState.PendingModeration, result.State);
            Assert.Equal(100, result.Progress);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Upload_RejectsUnsupportedExtensionBeforeNetworkCall()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lumen-creator-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(path, "bad");
        try
        {
            using var client = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException("network should not be used")));
            var service = new RemoteCreatorService(client, NullLogger<RemoteCreatorService>.Instance);
            var submission = new CreatorSubmission { FilePath = path, FileSizeBytes = 3, Title = "Bad" };
            await Assert.ThrowsAsync<NotSupportedException>(() => service.UploadAsync(submission));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Upload_CancellationSetsCancelledState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lumen-creator-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, new string('x', 100));
        try
        {
            using var client = new HttpClient(new RecordingHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            var service = new RemoteCreatorService(client, NullLogger<RemoteCreatorService>.Instance);
            var submission = new CreatorSubmission { FilePath = path, FileSizeBytes = 100, Title = "Cancel" };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(submission, ct: cts.Token));
            Assert.Equal(CreatorSubmissionState.Draft, submission.State);
        }
        finally { File.Delete(path); }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((request, _) => handler(request))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
