using System.Net;
using System.Net.Http;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YingView.Updates;

public enum UpdateCheckStatus { UpdateAvailable, UpToDate, NoWindowsRelease, StoreManaged, Failed }
public sealed record UpdateCheckResult(UpdateCheckStatus Status, Version? Version = null, string? ReleaseName = null,
    string? ReleaseUrl = null, string? AssetUrl = null, string? AssetName = null, string? Digest = null, string? Error = null);
public sealed record VerifiedUpdateDownload(string FilePath, Version Version, string AssetName, string Sha256);

/// <summary>Reads official GitHub releases and downloads only a digest-verified Windows installer.</summary>
public sealed class UpdateService : IDisposable
{
    public const string ReleasesApi = "https://api.github.com/repos/shixi-11/yingview/releases?per_page=10";
    private static readonly Regex SemVer = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.Compiled);
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _checkTimeout;
    private readonly string _downloadDirectory;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private const long MaxInstallerBytes = 512L * 1024 * 1024;

    public UpdateService(HttpMessageHandler? handler = null, TimeSpan? timeout = null, string? downloadDirectory = null)
    {
        _checkTimeout = timeout ?? TimeSpan.FromSeconds(20);
        _downloadDirectory = downloadDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "updates");
        var transport = handler ?? new HttpClientHandler { AllowAutoRedirect = false };
        _client = new HttpClient(transport, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsClient = true;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("YingView-UpdateCheck/1.0");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, string language, bool storeManaged = false, CancellationToken cancellationToken = default)
    {
        if (storeManaged) return new(UpdateCheckStatus.StoreManaged);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            using var response = await _client.GetAsync(ReleasesApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Unexpected GitHub release response.");
            var expectedAsset = language == "zh" ? "YingView-zh-CN.exe" : "YingView-en-US.exe";
            var candidates = new List<(Version Version, string Tag, string? Name, string? HtmlUrl, string? Url, string? Digest)>();
            foreach (var release in json.RootElement.EnumerateArray())
            {
                if (Bool(release, "draft") || Bool(release, "prerelease")) continue;
                var tag = String(release, "tag_name") ?? "";
                if (!TryParseStrictVersion(tag, out var version)) continue;
                var releaseName = String(release, "name");
                var releaseUrl = String(release, "html_url");
                if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!string.Equals(String(asset, "name"), expectedAsset, StringComparison.Ordinal)) continue;
                    var url = String(asset, "browser_download_url");
                    var digest = String(asset, "digest");
                    candidates.Add((version, tag, releaseName, releaseUrl, url, digest));
                    break;
                }
            }
            var best = candidates.OrderByDescending(x => x.Version).FirstOrDefault();
            if (best.Version is null) return new(UpdateCheckStatus.NoWindowsRelease);
            if (best.Version <= currentVersion) return new(UpdateCheckStatus.UpToDate, best.Version, best.Name ?? best.Tag);
            if (!IsTrustedAssetUrl(best.Url, expectedAsset) || !IsSha256Digest(best.Digest) || !IsTrustedReleaseUrl(best.HtmlUrl))
                return new(UpdateCheckStatus.Failed, Error: "Release asset URL or SHA-256 digest is missing or invalid.");
            return new(UpdateCheckStatus.UpdateAvailable, best.Version, best.Name ?? best.Tag, best.HtmlUrl, best.Url, expectedAsset, best.Digest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(UpdateCheckStatus.Failed, Error: "The update check timed out."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(UpdateCheckStatus.Failed, Error: ex.Message); }
    }

    public async Task<VerifiedUpdateDownload> DownloadVerifiedAsync(UpdateCheckResult update, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        if (update.Status != UpdateCheckStatus.UpdateAvailable || update.Version is null || !IsTrustedAssetUrl(update.AssetUrl, update.AssetName) || !IsSha256Digest(update.Digest))
            throw new InvalidOperationException("The update release asset has not passed validation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        var targetDirectory = _downloadDirectory;
        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, $"YingView-{update.Version}-{update.AssetName}");
        var temporary = target + ".download";
        try
        {
            using var response = await GetInstallerResponseAsync(update.AssetUrl!, update.AssetName!, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxInstallerBytes)
                throw new InvalidDataException("The installer exceeds the 512 MiB download limit.");
            long received = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > MaxInstallerBytes) throw new InvalidDataException("The installer exceeds the 512 MiB download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    if (response.Content.Headers.ContentLength is long total && total > 0) progress?.Report(Math.Clamp((double)received / total, 0, 1));
                }
            }
            string hash;
            await using (var file = File.OpenRead(temporary))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token).ConfigureAwait(false)).ToLowerInvariant();
            var expected = update.Digest!["sha256:".Length..];
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(expected)))
                throw new InvalidDataException("The downloaded installer failed its SHA-256 check.");
            File.Move(temporary, target, overwrite: true);
            progress?.Report(1);
            return new(target, update.Version, update.AssetName!, hash);
        }
        catch { try { File.Delete(temporary); } catch { } throw; }
    }

    private async Task<HttpResponseMessage> GetInstallerResponseAsync(string initialUrl, string exactName, CancellationToken cancellationToken)
    {
        if (!IsTrustedAssetUrl(initialUrl, exactName)) throw new InvalidDataException("The initial installer URL is not an official release asset.");
        var current = new Uri(initialUrl);
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null || redirects >= 5) throw new HttpRequestException("The installer exceeded the redirect limit or returned an invalid redirect.");
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsTrustedRedirect(next)) throw new HttpRequestException("The installer redirected outside the official GitHub asset hosts.");
                current = next;
                continue;
            }
            if (response.RequestMessage?.RequestUri is not Uri finalUri ||
                !(IsTrustedAssetUrl(finalUri.ToString(), exactName) || IsTrustedRedirect(finalUri)))
            {
                response.Dispose();
                throw new HttpRequestException("The installer response came from an untrusted host.");
            }
            return response;
        }
    }

    public static bool TryParseStrictVersion(string value, out Version version)
    {
        version = null!;
        var candidate = value.StartsWith('v') ? value[1..] : value;
        if (candidate.Contains('-')) return false;
        if (!SemVer.IsMatch(candidate)) return false;
        var core = candidate.Split('-', '+')[0];
        var parts = core.Split('.');
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch)) return false;
        version = new Version(major, minor, patch); return true;
    }

    public static bool IsTrustedAssetUrl(string? url, string? exactName)
    {
        if (exactName is not ("YingView-zh-CN.exe" or "YingView-en-US.exe") || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/shixi-11/yingview/releases/download/", StringComparison.Ordinal) &&
            uri.AbsolutePath.EndsWith("/" + exactName, StringComparison.Ordinal);
    }

    private static bool IsTrustedRedirect(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
    public static bool IsTrustedReleaseUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/shixi-11/yingview/releases/tag/", StringComparison.Ordinal);

    private static bool IsSha256Digest(string? digest) => digest is not null && Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$");
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? String(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
