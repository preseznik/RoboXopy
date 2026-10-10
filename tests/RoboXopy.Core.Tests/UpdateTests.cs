using System.Net;
using System.Security.Cryptography;
using System.Text;
using RoboXopy.Core.Updates;

namespace RoboXopy.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("1.2.3+dev", "1.2.3")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    public void Parse(string text, string expected) => Assert.Equal(expected, SemanticVersion.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-a..b")]
    [InlineData("-1.2.3")]
    [InlineData("release")]
    public void Parse_RejectsInvalid(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.1.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void Ordering(string lower, string higher)
    {
        Assert.True(SemanticVersion.Parse(lower) < SemanticVersion.Parse(higher));
        Assert.True(SemanticVersion.Parse(higher) > SemanticVersion.Parse(lower));
    }

    [Fact]
    public void Equality_IgnoresPrefixAndMetadata()
    {
        Assert.Equal(SemanticVersion.Parse("v1.1.0"), SemanticVersion.Parse("1.1.0+dev"));
        Assert.True(SemanticVersion.Parse("1.1.0") == SemanticVersion.Parse("1.1"));
    }
}

public class ReleaseInfoTests
{
    internal const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    internal static string ReleaseJson(string tag = "v1.2.0", bool draft = false, string? digest = "sha256:" + Hash) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "RoboXopy 1.2.0",
          "body": "## Changes\n- Things",
          "html_url": "https://github.com/preseznik/RoboXopy/releases/tag/{{tag}}",
          "published_at": "2026-10-10T08:00:00Z",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": false,
          "assets": [
            { "name": "RoboXopy-1.2.0-win-x64-setup.exe", "size": 1234,
              "browser_download_url": "https://example.test/RoboXopy-1.2.0-win-x64-setup.exe"
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} },
            { "name": "RoboXopy-1.2.0-win-x64-setup.exe.sha256", "size": 99,
              "browser_download_url": "https://example.test/RoboXopy-1.2.0-win-x64-setup.exe.sha256" },
            { "name": "RoboXopy-1.2.0-win-arm64-setup.exe", "size": 1234,
              "browser_download_url": "https://example.test/RoboXopy-1.2.0-win-arm64-setup.exe" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsReleaseAndAssets()
    {
        var release = ReleaseInfo.Parse(ReleaseJson())!;
        Assert.Equal("1.2.0", release.Version.ToString());
        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal("RoboXopy 1.2.0", release.Title);
        Assert.Contains("Things", release.Notes);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal(3, release.Assets.Count);
        Assert.Equal(Hash, release.Assets[0].Sha256);
        Assert.Null(release.Assets[1].Sha256);
    }

    [Fact]
    public void Parse_SkipsDraftsAndNonVersionTags()
    {
        Assert.Null(ReleaseInfo.Parse(ReleaseJson(draft: true)));
        Assert.Null(ReleaseInfo.Parse(ReleaseJson(tag: "nightly")));
    }

    [Fact]
    public void FindInstaller_MatchesRuntime()
    {
        var release = ReleaseInfo.Parse(ReleaseJson())!;
        Assert.Equal("RoboXopy-1.2.0-win-x64-setup.exe", release.FindInstaller("win-x64")!.Name);
        Assert.Equal("RoboXopy-1.2.0-win-arm64-setup.exe", release.FindInstaller("win-arm64")!.Name);
        Assert.Null(release.FindInstaller("win-x86"));

        var installer = release.FindInstaller("win-x64")!;
        Assert.Equal("RoboXopy-1.2.0-win-x64-setup.exe.sha256", release.FindChecksumFile(installer)!.Name);
        Assert.Null(release.FindChecksumFile(release.FindInstaller("win-arm64")!));
    }

    [Theory]
    [InlineData(Hash + "  setup.exe\n", "setup.exe", Hash)]
    [InlineData(Hash + " *SETUP.EXE", "setup.exe", Hash)]
    [InlineData(Hash, "setup.exe", Hash)]
    [InlineData("# comment\n" + Hash + "  other.exe\n", "setup.exe", null)]
    [InlineData("nothex  setup.exe", "setup.exe", null)]
    public void ParseChecksumFile(string content, string file, string? expected) =>
        Assert.Equal(expected, ReleaseInfo.ParseChecksumFile(content, file));

    [Theory]
    [InlineData("sha256:" + Hash, Hash)]
    [InlineData("SHA256:" + "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", Hash)]
    [InlineData("sha512:" + Hash, null)]
    [InlineData("sha256:abc", null)]
    [InlineData(null, null)]
    public void ParseDigest(string? digest, string? expected) => Assert.Equal(expected, ReleaseInfo.ParseDigest(digest));
}

public sealed class UpdateClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rx-upd-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task GetLatestRelease_SendsRequiredHeaders()
    {
        var handler = new FakeHandler(_ => Text(ReleaseInfoTests.ReleaseJson()));
        using var client = new UpdateClient("1.1.1+abc", "owner/repo", handler);

        var release = await client.GetLatestReleaseAsync();

        Assert.Equal("1.2.0", release.Version.ToString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri!.ToString());
        Assert.Equal("RoboXopy/1.1.1", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
    }

    [Fact]
    public async Task GetLatestRelease_ExplainsFailures()
    {
        using var notFound = new UpdateClient("1.0.0", handler: new FakeHandler(_ => Text("{}", HttpStatusCode.NotFound)));
        Assert.Contains("No published releases", (await Assert.ThrowsAsync<UpdateException>(() => notFound.GetLatestReleaseAsync())).Message);

        using var limited = new UpdateClient("1.0.0", handler: new FakeHandler(_ =>
        {
            var response = Text("{}", HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        }));
        Assert.Contains("limit", (await Assert.ThrowsAsync<UpdateException>(() => limited.GetLatestReleaseAsync())).Message);

        using var garbage = new UpdateClient("1.0.0", handler: new FakeHandler(_ => Text("<html>")));
        await Assert.ThrowsAsync<UpdateException>(() => garbage.GetLatestReleaseAsync());

        using var offline = new UpdateClient("1.0.0", handler: new FakeHandler(_ => throw new HttpRequestException("No route")));
        Assert.Contains("No route", (await Assert.ThrowsAsync<UpdateException>(() => offline.GetLatestReleaseAsync())).Message);
    }

    [Fact]
    public async Task ExpectedSha256_PrefersDigest_ThenChecksumFile()
    {
        var other = new string('f', 64);
        var handler = new FakeHandler(_ => Text($"{other}  RoboXopy-1.2.0-win-x64-setup.exe\n"));
        using var client = new UpdateClient("1.0.0", handler: handler);

        var withDigest = ReleaseInfo.Parse(ReleaseInfoTests.ReleaseJson())!;
        Assert.Equal(ReleaseInfoTests.Hash, await client.GetExpectedSha256Async(withDigest, withDigest.FindInstaller("win-x64")!));
        Assert.Empty(handler.Requests);

        var withoutDigest = ReleaseInfo.Parse(ReleaseInfoTests.ReleaseJson(digest: null))!;
        Assert.Equal(other, await client.GetExpectedSha256Async(withoutDigest, withoutDigest.FindInstaller("win-x64")!));
        Assert.EndsWith(".sha256", Assert.Single(handler.Requests).RequestUri!.ToString());

        await Assert.ThrowsAsync<UpdateException>(() =>
            client.GetExpectedSha256Async(withoutDigest, withoutDigest.FindInstaller("win-arm64")!));
    }

    [Fact]
    public async Task Download_VerifiesHash()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 300_000));
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        using var client = new UpdateClient("1.0.0", handler: handler);
        var asset = new ReleaseAsset("setup.exe", new Uri("https://example.test/setup.exe"), payload.Length, sha);

        var reports = new List<DownloadProgress>();
        var path = await client.DownloadAsync(asset, sha, _dir, new SyncProgress(reports.Add));

        Assert.Equal(Path.Combine(_dir, "setup.exe"), path);
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".partial"));
        Assert.Equal(1.0, reports[^1].Fraction);

        // A verified file from an earlier download is reused.
        Assert.Equal(path, await client.DownloadAsync(asset, sha, _dir));
        Assert.Single(handler.Requests);

        // A mismatching download is discarded.
        File.Delete(path);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(asset, new string('0', 64), _dir));
        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_RejectsPathsInAssetName()
    {
        using var client = new UpdateClient("1.0.0", handler: new FakeHandler(_ => Text("")));
        var asset = new ReleaseAsset(@"..\evil.exe", new Uri("https://example.test/x"), 1, null);
        await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(asset, new string('0', 64), _dir));
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
