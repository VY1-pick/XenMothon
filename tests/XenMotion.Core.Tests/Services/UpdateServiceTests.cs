using Xunit;
using XenMotion.Core.Models;
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

    [Fact]
    public async Task PlainHttpManifestAndDownloadAreRejected()
    {
        using var client = CreateClient(new { version="2.0.0", downloadUrl="http://cdn.example.com/setup.exe" });
        var http = new RemoteUpdateService(client,"http://example.com/manifest.json",NullLogger<RemoteUpdateService>.Instance,"0.9.0-beta.1");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>http.CheckForUpdateAsync());
        var https = new RemoteUpdateService(client,"https://example.com/manifest.json",NullLogger<RemoteUpdateService>.Instance,"0.9.0-beta.1");
        await Assert.ThrowsAsync<InvalidDataException>(()=>https.CheckForUpdateAsync());
    }

    [Fact]
    public async Task GitHubReleasesChooseNewestPublishedBetaWithUploadedInstaller()
    {
        using var client = CreateClient(new[]
        {
            GitHubRelease("v0.9.0-beta.99", draft: true),
            GitHubRelease("v0.9.0-beta.100", state: "starter"),
            GitHubRelease("v0.9.0-beta.9"),
            GitHubRelease("v0.9.0-beta.10"),
            GitHubRelease("not-a-version")
        });
        var service = new RemoteUpdateService(client, ApplicationSettings.DefaultUpdateManifestUrl, NullLogger<RemoteUpdateService>.Instance, "0.9.0-beta.8");
        var update = await service.CheckForUpdateAsync();
        Assert.NotNull(update);
        Assert.Equal("0.9.0-beta.10", update.Version);
        Assert.Equal("https://github.com/VY1-pick/XenMothon/releases/download/v0.9.0-beta.10/XenMotion-0.9.0-beta.10-Setup-x64.exe", update.DownloadUrl);
        Assert.Equal("https://github.com/VY1-pick/XenMothon/releases/tag/v0.9.0-beta.10", update.ReleaseNotesUrl);
    }

    [Theory]
    [InlineData("0.9.0-beta.5")]
    [InlineData("0.9.0-beta.6")]
    [InlineData("1.0.0")]
    public async Task GitHubReleasesDoNotOfferSameOrOlderInstaller(string installed)
    {
        using var client = CreateClient(new[] { GitHubRelease("v0.9.0-beta.5") });
        var service = new RemoteUpdateService(client, ApplicationSettings.DefaultUpdateManifestUrl, NullLogger<RemoteUpdateService>.Instance, installed);
        Assert.Null(await service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task GitHubReleasesRejectUnsafeInstallerUrls()
    {
        using var client = CreateClient(new[] { GitHubRelease("v1.0.0", downloadUrl: "file:///C:/fake-setup.exe") });
        var service = new RemoteUpdateService(client, ApplicationSettings.DefaultUpdateManifestUrl, NullLogger<RemoteUpdateService>.Instance, "0.9.0-beta.5");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task EmptyGitHubReleaseFeedDoesNotOfferAnUpdate()
    {
        using var client = CreateClient(Array.Empty<object>());
        var service = new RemoteUpdateService(client, ApplicationSettings.DefaultUpdateManifestUrl, NullLogger<RemoteUpdateService>.Instance, "0.9.0-beta.5");
        Assert.Null(await service.CheckForUpdateAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://raw.githubusercontent.com/VY1-pick/XenMothon/main/docs/updates/update.json")]
    public void SettingsMigrateOldDefaultWithoutEnablingDisabledUpdates(string source)
    {
        var settings = new ApplicationSettings { UpdateManifestUrl = source, CheckForUpdates = false };
        settings.Normalize();
        Assert.Equal(ApplicationSettings.DefaultUpdateManifestUrl, settings.UpdateManifestUrl);
        Assert.False(settings.CheckForUpdates);
    }

    [Fact]
    public void SettingsKeepCustomUpdateSource()
    {
        var settings = new ApplicationSettings { UpdateManifestUrl = "https://example.com/custom.json" };
        settings.Normalize();
        Assert.Equal("https://example.com/custom.json", settings.UpdateManifestUrl);
    }

    [Theory]
    [InlineData("0.9.0-beta.5", "0.1.2-beta")]
    [InlineData("0.1.1-beta", "0.1.2-beta")]
    [InlineData("0.1.2-beta", null)]
    [InlineData("0.1.2", null)]
    [InlineData("0.1.3-beta", null)]
    public async Task GitHubNumberingResetDoesNotOfferLegacyBetaAsAnUpgrade(string installed, string? expected)
    {
        using var client = CreateClient(new[] { GitHubRelease("v0.9.0-beta.5"), GitHubRelease("v0.1.2-beta") });
        var service = new RemoteUpdateService(client, ApplicationSettings.DefaultUpdateManifestUrl, NullLogger<RemoteUpdateService>.Instance, installed);
        Assert.Equal(expected, (await service.CheckForUpdateAsync())?.Version);
    }

    [Fact]
    public async Task CustomManifestKeepsNumericOrderingAcrossGitHubNumberingReset()
    {
        using var client = CreateClient(new { version = "0.1.2-beta", downloadUrl = "https://example.com/setup.exe" });
        var service = new RemoteUpdateService(client, "https://example.com/update.json", NullLogger<RemoteUpdateService>.Instance, "0.9.0-beta.5");
        Assert.Null(await service.CheckForUpdateAsync());
    }

    private static object GitHubRelease(string tag, bool draft = false, string state = "uploaded", string? downloadUrl = null)
    {
        string version = tag.TrimStart('v', 'V');
        return new
        {
            tag_name = tag, draft, prerelease = tag.Contains("beta"),
            html_url = $"https://github.com/VY1-pick/XenMothon/releases/tag/{tag}",
            published_at = "2026-10-18T18:01:22Z",
            assets = new[] { new { name = $"XenMotion-{version}-Setup-x64.exe", state,
                browser_download_url = downloadUrl ?? $"https://github.com/VY1-pick/XenMothon/releases/download/{tag}/XenMotion-{version}-Setup-x64.exe" } }
        };
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
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.Contains("XenMotion", request.Headers.UserAgent.ToString());
                Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
