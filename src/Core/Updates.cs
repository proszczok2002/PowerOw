using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerOw;

internal sealed record UpdateRelease(Version Version, Uri Address, long Size, string Sha256, string Notes);

internal sealed class Updates(HttpClient client)
{
    internal const string Repository = "https://github.com/proszczok2002/PowerOw";
    internal const string InstallerName = "PowerOw-Setup.exe";
    private static readonly Uri Latest = new("https://api.github.com/repos/proszczok2002/PowerOw/releases/latest");
    private const int MaxMetadata = 512 * 1024;
    private const long MaxInstaller = 512L * 1024 * 1024;
    private static readonly HttpClient PublicClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static Updates Public { get; } = new(PublicClient);
    internal static Version CurrentVersion => typeof(Updates).Assembly.GetName().Version!;
    internal static string DisplayVersion(Version version) => "v" + version.ToString(version.Build > 0 ? 3 : 2);
    internal static string WindowTitle => "PowerOw " + DisplayVersion(CurrentVersion);

    internal async Task<UpdateRelease?> Check(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = Request(Latest);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[16384];
        int length;
        while ((length = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (data.Length + length > MaxMetadata)
                throw new InvalidDataException();
            data.Write(buffer, 0, length);
        }
        return ParseRelease(data.GetBuffer().AsSpan(0, (int)data.Length), CurrentVersion);
    }

    internal static UpdateRelease? ParseRelease(ReadOnlySpan<byte> data, Version installed)
    {
        var release = JsonSerializer.Deserialize<Release>(data) ?? throw new InvalidDataException();
        string? tag = release.Tag;
        if (release.Draft || release.Prerelease || tag is null || !tag.StartsWith('v') ||
            !Version.TryParse(tag.AsSpan(1), out var version) || version.Revision >= 0 ||
            tag != "v" + version.ToString())
            return null;
        var normalized = new Version(version.Major, version.Minor, Math.Max(0, version.Build), 0);
        if (normalized <= new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build), 0))
            return null;
        var matches = release.Assets?.Where(asset => asset.Name == InstallerName).ToArray();
        if (matches is not { Length: 1 } || matches[0] is not { Address: not null, Digest: not null } asset ||
            asset.Address != $"{Repository}/releases/download/{tag}/{InstallerName}" ||
            asset.Size is <= 0 or > MaxInstaller || !asset.Digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            asset.Digest.Length != 71 || !asset.Digest[7..].All(char.IsAsciiHexDigit))
            throw new InvalidDataException();
        return new(normalized, new Uri(asset.Address), asset.Size, asset.Digest[7..], release.Body ?? "");
    }

    internal async Task Download(UpdateRelease release, string file, IProgress<int> progress, CancellationToken cancellation)
    {
        var address = release.Address;
        for (int redirects = 0; redirects < 5; redirects++)
        {
            using var request = Request(address);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException();
                address = new Uri(address, location);
                if (!AllowedRedirect(address))
                    throw new InvalidDataException();
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException();
            using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long written = 0;
            int last = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
            {
                written += read;
                if (written > release.Size)
                    throw new InvalidDataException();
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                int percent = (int)(written * 100 / release.Size);
                if (percent != last)
                    progress.Report(last = percent);
            }
            if (written != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();
            return;
        }
        throw new InvalidDataException();
    }

    internal static bool AllowedRedirect(Uri address) => address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort &&
        address.UserInfo.Length == 0 && address.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    internal static string NotesFor(UpdateRelease release, string language)
    {
        var sections = release.Notes.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        int firstHeading = Array.FindIndex(sections, line => line.StartsWith("## ", StringComparison.Ordinal));
        var languages = TextCatalog.Languages;
        string title = languages[Math.Max(0, Array.IndexOf(TextCatalog.Codes, language))];
        int from = Array.FindIndex(sections, line => line.Trim() == "## " + title);
        if (from < 0)
            from = Array.FindIndex(sections, line => line.Trim() == "## Polski");
        if (from < 0 && firstHeading >= 0 && languages.Contains(sections[firstHeading][3..].Trim()))
            from = firstHeading;
        string notes = (from < 0 ? release.Notes : string.Join(Environment.NewLine, sections.Skip(from + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)))).Trim();
        return notes.Length > 1200 ? notes[..1200] + "…" : notes;
    }

    private static HttpRequestMessage Request(Uri address)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("PowerOw/" + CurrentVersion.ToString(3));
        return request;
    }

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] Asset[]? Assets);

    private sealed record Asset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? Address,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
}
