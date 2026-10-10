using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace RoboXopy.Core.Updates;

/// <summary>An update check or download failed; the message is meant for the user.</summary>
public sealed class UpdateException(string message, Exception? innerException = null) : Exception(message, innerException);

public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes, 0, 1) : 0;
}

/// <summary>Finds the latest RoboXopy release on GitHub and downloads its installer.</summary>
public sealed class UpdateClient : IDisposable
{
    public const string DefaultRepository = "preseznik/RoboXopy";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;

    /// <param name="appVersion">Sent in the User-Agent header, which the GitHub API requires.</param>
    /// <param name="repository">GitHub "owner/name".</param>
    /// <param name="handler">Replaces the network stack (for tests).</param>
    public UpdateClient(string appVersion, string repository = DefaultRepository, HttpMessageHandler? handler = null)
    {
        Repository = repository;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RoboXopy", appVersion.Split('+')[0]));
    }

    public string Repository { get; }

    public Uri ReleasesPage => new($"https://github.com/{Repository}/releases/latest");

    /// <summary>The newest published release (drafts and prereleases are skipped by GitHub).</summary>
    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException("No published releases were found.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            {
                throw new UpdateException("GitHub's limit on update checks from this network was reached. Try again in an hour.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ReleaseInfo.Parse(json) ?? throw new UpdateException("The latest release isn't tagged with a version number.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("GitHub didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Couldn't reach GitHub. {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new UpdateException("GitHub sent an answer RoboXopy couldn't read.", ex);
        }
    }

    /// <summary>The SHA-256 the installer must have: GitHub's digest, or the release's .sha256 file.</summary>
    public async Task<string> GetExpectedSha256Async(ReleaseInfo release, ReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        if (asset.Sha256 is not null) return asset.Sha256;

        var checksumFile = release.FindChecksumFile(asset)
            ?? throw new UpdateException($"Release {release.Version} doesn't publish a checksum for {asset.Name}, so the download can't be verified.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var text = await _http.GetStringAsync(checksumFile.DownloadUrl, timeout.Token).ConfigureAwait(false);
            return ReleaseInfo.ParseChecksumFile(text, asset.Name)
                ?? throw new UpdateException($"{checksumFile.Name} doesn't contain a SHA-256 for {asset.Name}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("GitHub didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Couldn't download {checksumFile.Name}. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="directory"/> and checks its SHA-256. A matching file
    /// left by an earlier download is reused.
    /// </summary>
    /// <returns>The full path of the verified file.</returns>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string expectedSha256, string directory,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!ReleaseInfo.IsSha256(expectedSha256)) throw new ArgumentException("Not a SHA-256 hash.", nameof(expectedSha256));
        expectedSha256 = expectedSha256.ToLowerInvariant();
        if (asset.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || asset.Name is "." or "..")
        {
            throw new UpdateException($"'{asset.Name}' isn't a valid file name.");
        }

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, asset.Name);
        if (File.Exists(target) && await HashFileAsync(target, cancellationToken).ConfigureAwait(false) == expectedSha256)
        {
            var length = new FileInfo(target).Length;
            progress?.Report(new DownloadProgress(length, length));
            return target;
        }

        var partial = target + ".partial";
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            stall.CancelAfter(RequestTimeout);
            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"The download failed: GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var total = response.Content.Headers.ContentLength ?? asset.Size;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0, reported = 0;
                progress?.Report(new DownloadProgress(0, total));
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    var read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    if (received - reported >= Math.Max(total / 200, 64 * 1024))
                    {
                        reported = received;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }

                progress?.Report(new DownloadProgress(received, Math.Max(total, received)));
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != expectedSha256)
            {
                throw new UpdateException("The downloaded installer didn't match its published checksum, so it was discarded. Try again.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new UpdateException("The download stalled. Check your connection and try again.");
            }

            if (ex is HttpRequestException or IOException) throw new UpdateException($"The download failed. {ex.Message}", ex);
            throw;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _http.Dispose();
}
