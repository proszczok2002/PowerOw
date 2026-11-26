using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PowerOw;

internal static partial class Checks
{
    private static void CheckUpdateBehavior()
    {
        Version installed = new(1, 0, 0, 0);
        byte[] fixture = [3, 1, 4, 1, 5, 9];
        string digest = Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant();
        string url = Updates.Repository + "/releases/download/v1.0.1/" + Updates.InstallerName;
        byte[] Json(string tag = "v1.0.1", string? urlOverride = null, string? digestOverride = null, bool draft = false,
            bool preview = false, long? size = null) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = tag, draft, prerelease = preview, body = "## Polski\nNaprawa.\n## English\nFix.",
            assets = new[] { new { name = Updates.InstallerName, size = size ?? fixture.Length,
                browser_download_url = urlOverride ?? url, digest = digestOverride ?? ("sha256:" + digest) } }
        });
        var update = Updates.ParseRelease(Json(), installed)!;
        Require(update.Version == new Version(1, 0, 1, 0), "A newer release is offered");
        Require(Updates.DisplayVersion(installed) == "v1.0" && Updates.DisplayVersion(update.Version) == "v1.0.1", "Version captions");
        Require(Updates.NotesFor(update, "pl") == "Naprawa." && Updates.NotesFor(update, "en") == "Fix."
            && Updates.NotesFor(update, "de") == "Naprawa.", "Localized release note selection");
        foreach (string tag in new[] { "v1.0.0", "v1.0", "v0.9", "v1.0.1-beta", "v01.0.1", "v1.0.1.0" })
            Require(Updates.ParseRelease(Json(tag), installed) is null, $"Ignore tag {tag}");
        Require(Updates.ParseRelease(Json(draft: true), installed) is null &&
            Updates.ParseRelease(Json(preview: true), installed) is null, "Ignore drafts and previews");
        Reject(() => Updates.ParseRelease(Json(urlOverride: url.Replace("github.com/", "example.org/")), installed));
        Reject(() => Updates.ParseRelease(Json(digestOverride: "sha256:" + new string('x', 64)), installed));
        Reject(() => Updates.ParseRelease(Json(size: 0), installed));
        Require(!Updates.AllowedRedirect(new Uri("http://release-assets.githubusercontent.com/path")) &&
            !Updates.AllowedRedirect(new Uri("https://release-assets.githubusercontent.com.example.org/path")) &&
            !Updates.AllowedRedirect(new Uri("https://name@release-assets.githubusercontent.com/path")), "Strict HTTPS redirect host");

        var defaults = JsonSerializer.Deserialize<Preferences>("""{"Language":"pl","Theme":0}""")!;
        Require(defaults.AutomaticUpdates && defaults.UpdateIntervalMinutes == 15, "Old preferences retain auto-check defaults");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Require(UpdateSession.ShouldCheck(true, true, false, now, now), "Protection permits a scheduled check in the tray");
        Require(!UpdateSession.ShouldCheck(true, true, false, now, now.AddMinutes(15)), "Wait for configured interval");
        Require(!UpdateSession.ShouldCheck(true, false, false, now, now), "No tray check while protection is paused");
        Require(UpdateSession.ShouldCheck(true, false, true, now, now.AddMinutes(15)), "Check when opened with protection paused");
        Require(!UpdateSession.ShouldCheck(false, true, true, now, now), "Automatic checking can be turned off");

        string firstFile = Path.Combine(Path.GetTempPath(), "PowerOw-Audit-" + Guid.NewGuid().ToString("N"));
        string wrongFile = firstFile + ".wrong";
        try
        {
            using var client = new HttpClient(new ReleaseResponse(request =>
            {
                if (request.RequestUri!.Host == "github.com")
                    return new HttpResponseMessage(HttpStatusCode.Redirect)
                        { Headers = { Location = new Uri("https://release-assets.githubusercontent.com/verified") } };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture) };
            }));
            var updates = new Updates(client);
            var progress = new AuditProgress();
            updates.Download(update, firstFile, progress, CancellationToken.None).GetAwaiter().GetResult();
            Require(File.ReadAllBytes(firstFile).SequenceEqual(fixture) && progress.Last == 100, "Verified download and 100 percent");
            Reject(() => updates.Download(update with { Sha256 = new string('0', 64) }, wrongFile, progress, CancellationToken.None)
                .GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(firstFile);
            File.Delete(wrongFile);
        }
        Console.WriteLine("Update checks passed for release metadata, SHA-256, redirected download and protection/interval settings.");

        static void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException)
            {
                Require(true, "Invalid update refused");
                return;
            }
            Require(false, "Invalid update was accepted");
        }
    }

    private sealed class ReleaseResponse(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class AuditProgress : IProgress<int>
    {
        internal int Last { get; private set; }
        public void Report(int value) => Last = value;
    }
}
