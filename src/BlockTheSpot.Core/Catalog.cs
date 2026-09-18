using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlockTheSpot.Core;

public static class Sources
{
    public const string Repository = "https://github.com/RobyRew/BlockTheSpot-Installer";
    public static readonly Uri Catalog = new("https://raw.githubusercontent.com/LoaderSpot/table/main/table/versions.json");
    public static readonly Uri CatalogApi = new("https://robyrew.github.io/BlockTheSpot-Installer/api/v1/windows-x64.json");
    public static readonly Uri Config = new("https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/config.ini");
    public static readonly Uri Chrome = new("https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/chrome_elf.dll");
    public static readonly Uri Block = new("https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/blockthespot.dll");
    // Spotify's only permanent, unsigned installer URL. It always serves the current version.
    public static readonly Uri LatestSpotify = new("https://download.scdn.co/SpotifyFullSetupX64.exe");
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/RobyRew/BlockTheSpot-Installer/releases/latest");
    // Versioned installers: Spotify hands the client a signed, short-lived upgrade.scdn.co link
    // for the current version only (spclient desktop-update/v2, authenticated). Older links expire
    // with HTTP 403, so a catalog entry keeps Spotify's link as the first attempt and the LoadSpot
    // mirror as the fallback. Every download is still checked against Spotify's Authenticode signature.
    public const string MirrorHost = "loadspot.amd64fox1.workers.dev";
    public const string UpgradeHost = "upgrade.scdn.co";
}

/// <summary>One installable Spotify build. <see cref="Url"/> is tried first; <see cref="Mirror"/> is the fallback.</summary>
public sealed record SpotifyChoice(string? FullVersion, Uri Url, string? Date = null, long Size = 0, bool Recommended = false, Uri? Mirror = null, bool Custom = false)
{
    public static SpotifyChoice Latest { get; } = new(null, Sources.LatestSpotify);
    public bool IsLatest => FullVersion is null;
    public string Title => FullVersion ?? "Latest official Spotify";
    public string Badge => Recommended ? "Tested" : IsLatest ? "Official" : Custom ? "Custom" : "Untested";
    public string Source => SpotifyVersions.IsOfficial(Url) ? Mirror is null ? "Spotify" : "Spotify · mirror fallback" : "LoadSpot mirror";
    public string Detail => string.Join(" · ", new[] { IsLatest ? "Current release" : Date, Size > 0 ? $"{Size / 1048576d:F0} MiB" : null, Source }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public IEnumerable<Uri> Urls => Mirror is null || Mirror == Url ? [Url] : [Url, Mirror];
    public override string ToString() => Title;
}

public sealed record CatalogResult(string? Tested, IReadOnlyList<SpotifyChoice> Choices, SpotifyChoice Selected, string? Warning = null);

public static partial class SpotifyVersions
{
    [GeneratedRegex(@"^1\.\d+\.\d+\.\d+(?:\.g[0-9a-fA-F]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^1\.\d{1,5}\.\d{1,5}\.\d{1,8}\.g[0-9a-fA-F]{8,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex FullVersionPattern();

    public static Version Parse(string text)
    {
        if (!VersionPattern().IsMatch(text)) throw new InvalidDataException($"Unrecognized Spotify version: {text}");
        if (!Version.TryParse(string.Join('.', text.Split('.').Take(4)), out var version))
            throw new InvalidDataException($"Unrecognized Spotify version: {text}");
        return version;
    }

    public static string MinimumFromConfig(string text) =>
        text.Split('\n').Select(line => line.Trim().TrimStart('﻿'))
            .Where(line => line.StartsWith(';')).Select(line => line[1..].Trim())
            .FirstOrDefault(line => VersionPattern().IsMatch(line))
        ?? throw new InvalidDataException("BlockTheSpot's configuration does not contain a supported Spotify version.");

    public static void ValidateInstalled(string installed, string minimum, SpotifyChoice? pinned)
    {
        var actual = Parse(installed);
        if (actual < Parse(minimum))
            throw new InvalidOperationException($"Spotify {installed} is older than the supported minimum {minimum}. Install a supported version before patching.");
        if (pinned?.FullVersion is { } target && actual != Parse(target))
            throw new InvalidOperationException($"Spotify {installed} is installed, but {target} was selected. Setup did not finish as expected; no patch was applied.");
    }

    private static bool IsPlainHttps(Uri url) =>
        url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0;

    /// <summary>A link on Spotify's own hosts: the permanent full installer or a versioned x64 upgrade package.</summary>
    public static bool IsOfficial(Uri url) => IsPlainHttps(url) && (url == Sources.LatestSpotify || VersionFromOfficial(url) is not null);

    public static bool IsMirror(Uri url, string version) =>
        IsPlainHttps(url) && url.Host == Sources.MirrorHost && url.AbsolutePath == $"/download/spotify_installer-{version}-x64.exe";

    public static bool IsCatalogDownload(Uri url, string version) =>
        IsMirror(url, version) || string.Equals(VersionFromOfficial(url), version, StringComparison.OrdinalIgnoreCase);

    public static Uri MirrorFor(string fullVersion) => new($"https://{Sources.MirrorHost}/download/spotify_installer-{fullVersion}-x64.exe");

    private static string? VersionFromOfficial(Uri url)
    {
        if (!IsPlainHttps(url) || url.Host != Sources.UpgradeHost) return null;
        var match = Regex.Match(url.AbsolutePath, @"^/upgrade/client/win32-x86_64/spotify_installer-(1\.\d+\.\d+\.\d+\.g[0-9a-fA-F]+)-[0-9]+\.exe$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Turns typed input into an installable choice: a full version such as 1.2.80.699.gd5f6ebe3
    /// (served by the mirror's stable filename) or a link on Spotify's or the mirror's host.
    /// The version and signature are still verified after download, so a wrong guess fails safely.
    /// </summary>
    public static SpotifyChoice? TryCustom(string? text)
    {
        var input = text?.Trim();
        if (string.IsNullOrEmpty(input)) return null;
        if (FullVersionPattern().IsMatch(input))
            return new(input, MirrorFor(input), Custom: true);
        if (!Uri.TryCreate(input, UriKind.Absolute, out var url) || !IsPlainHttps(url)) return null;
        if (url == Sources.LatestSpotify) return SpotifyChoice.Latest;
        if (VersionFromOfficial(url) is { } official) return new(official, url, Mirror: MirrorFor(official), Custom: true);
        var mirror = Regex.Match(url.AbsolutePath, @"^/download/spotify_installer-(1\.\d+\.\d+\.\d+\.g[0-9a-fA-F]+)-x64\.exe$", RegexOptions.CultureInvariant);
        return url.Host == Sources.MirrorHost && mirror.Success ? new(mirror.Groups[1].Value, url, Custom: true) : null;
    }

    /// <summary>
    /// Reads every Windows x64 release build. Accepts the LoadSpot table (one url per build), the legacy
    /// links schema, and the Pages feed, which may add "official" and "mirror" links next to "url".
    /// </summary>
    public static CatalogResult Read(string json, string tested)
    {
        var testedVersion = Parse(tested);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("LoadSpot returned an invalid version catalog.");
        var releases = new List<SpotifyChoice>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var entry = property.Value;
            var full = Text(entry, "fullversion");
            if (full is null || !VersionPattern().IsMatch(full)) continue;
            if (!Version.TryParse(string.Join('.', full.Split('.').Take(4)), out var number)) continue;
            if (number.ToString() != property.Name) continue;
            var build = Text(entry, "buildType");
            if (build is not null && !build.Equals("Release", StringComparison.OrdinalIgnoreCase)) continue;
            var x64 = Child(Child(entry, "win"), "x64");
            var links = new[] { Text(x64, "official"), x64.ValueKind == JsonValueKind.String ? x64.GetString() : Text(x64, "url"),
                    Text(x64, "mirror"), Text(Child(Child(entry, "links"), "win"), "x64") }
                .Select(link => Uri.TryCreate(link, UriKind.Absolute, out var url) && IsCatalogDownload(url, full) ? url : null)
                .Where(url => url is not null).Distinct().ToList();
            var official = links.FirstOrDefault(url => IsOfficial(url!));
            var mirror = links.FirstOrDefault(url => IsMirror(url!, full));
            if (official is null && mirror is null) continue;
            // Spotify expires its versioned links, so a build known only by such a link still gets
            // the mirror's stable filename as a fallback; a missing mirror file fails like any 404.
            mirror ??= SpotifyVersions.MirrorFor(full);
            var sizeElement = Child(x64, "size");
            long size = sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt64(out var bytes) ? Math.Max(0, bytes) : 0;
            var date = Text(x64, "date");
            if (date is not null && !DateTime.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = null;
            releases.Add(new(full, official ?? mirror!, date, size, number == testedVersion, official is null ? null : mirror));
        }
        releases.Sort((a, b) => Parse(b.FullVersion!).CompareTo(Parse(a.FullVersion!)));
        SpotifyChoice[] choices = [SpotifyChoice.Latest, .. releases];
        var selected = releases.FirstOrDefault(r => r.Recommended);
        return new(tested, choices, selected ?? SpotifyChoice.Latest,
            releases.Count == 0 ? "The catalog has no Windows x64 versions. The latest official installer is still available." :
            selected is null ? $"The catalog does not list the tested version {tested}. Its saved link is still available." : null);
    }

    private static JsonElement Child(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var result) ? result : default;
    private static string? Text(JsonElement element, string property) =>
        Child(element, property) is { ValueKind: JsonValueKind.String } child ? child.GetString() : null;
}

public sealed class CatalogService(Downloads downloads)
{
    public async Task<CatalogResult> LoadAsync(CancellationToken token)
    {
        // Browsing does not depend on the patch server. Installation validates its current config separately.
        string? failure = null;
        foreach (var (source, seconds) in new[] { (Sources.CatalogApi, 4), (Sources.Catalog, 8) })
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                var result = SpotifyVersions.Read(await downloads.TextAsync(source, deadline.Token), Compatibility.TestedVersion);
                if (result.Choices.Count < 2) throw new InvalidDataException("No usable versions in the catalog.");
                return result;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
            { failure = error.Message; }
        }
        token.ThrowIfCancellationRequested();
        return new(Compatibility.TestedVersion, [Compatibility.TestedChoice, SpotifyChoice.Latest], Compatibility.TestedChoice,
            $"Version list unavailable. The saved link for the tested version is still available. {failure}");
    }
}
