using Xunit;
using System.Net;
using XenMotion.Infrastructure.Persistence.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace XenMotion.Core.Tests.Services;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_NewVersion_ReturnsValidatedUpdate()
    {
        using var client = CreateClient(new
        {
            version = "2.0.0",
            downloadUrl = "https://cdn.example.com/XenMotion-2.0.0.zip",
            releaseNotesUrl = "https://example.com/releases/2.0.0",
            publishedAtUtc = "2026-10-02T12:00:00Z",
            isMandatory = true
        });
        var service = new RemoteUpdateService(client, "https://example.com/manifest.json", NullLogger<RemoteUpdateService>.Instance, currentVersion: "0.8.0");

        var result = await service.CheckForUpdateAsync();

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version);
        Assert.True(result.IsMandatory);
        Assert.Equal("https://cdn.example.com/XenMotion-2.0.0.zip", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_OlderVersion_ReturnsNull()
    {
        using var client = CreateClient(new
        {
            version = "0.0.1",
            downloadUrl = "https://cdn.example.com/XenMotion-0.0.1.zip"
        });
        var service = new RemoteUpdateService(client, "https://example.com/manifest.json", NullLogger<RemoteUpdateService>.Instance, currentVersion: "0.8.0");

        var result = await service.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_InvalidDownloadUrl_Throws()
    {
        using var client = CreateClient(new
        {
            version = "2.0.0",
            downloadUrl = "file:///C:/XenMotion.zip"
        });
        var service = new RemoteUpdateService(client, "https://example.com/manifest.json", NullLogger<RemoteUpdateService>.Instance, currentVersion: "0.8.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdateAsync_InvalidManifestUrl_Throws()
    {
        using var client = CreateClient(new { version = "2.0.0", downloadUrl = "https://cdn.example.com/XenMotion.zip" });
        var service = new RemoteUpdateService(client, "file:///manifest.json", NullLogger<RemoteUpdateService>.Instance, currentVersion: "0.8.0");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckForUpdateAsync());
    }

    private static HttpClient CreateClient(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var handler = new StubHandler(json);
        return new HttpClient(handler);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
