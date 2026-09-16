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
    public static readonly Uri LatestSpotify = new("https://download.scdn.co/SpotifyFullSetupX64.exe");
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/RobyRew/BlockTheSpot-Installer/releases/latest");
}

public sealed record SpotifyChoice(string? FullVersion, Uri Url, string? Date = null, long Size = 0, bool Recommended = false)
{
    public static SpotifyChoice Latest { get; } = new(null, Sources.LatestSpotify);
    public string Title => FullVersion ?? "Latest official Spotify";
    public string Badge => Recommended ? "Latest tested compatible" : FullVersion is null ? "Official · untested" : "Untested · x64";
    public string Detail => FullVersion is null ? "Current full installer · Spotify's servers" :
        string.Join(" · ", new[] { Date, Size > 0 ? $"{Size / 1048576d:F2} MiB" : null, "LoadSpot catalog" }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public override string ToString() => Title;
}

public sealed record CatalogResult(string? Minimum, IReadOnlyList<SpotifyChoice> Choices, SpotifyChoice Selected, string? Warning = null);

public static partial class SpotifyVersions
{
    [GeneratedRegex(@"^1\.\d+\.\d+\.\d+(?:\.g[0-9a-fA-F]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static Version Parse(string text)
    {
        if (!VersionPattern().IsMatch(text)) throw new InvalidDataException($"Unrecognized Spotify version: {text}");
        if (!Version.TryParse(string.Join('.', text.Split('.').Take(4)), out var version))
            throw new InvalidDataException($"Unrecognized Spotify version: {text}");
        return version;
    }

    public static string MinimumFromConfig(string text) =>
        text.Split('\n').Select(line => line.Trim().TrimStart('\uFEFF'))
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

    public static bool IsCatalogDownload(Uri url, string version)
    {
        if (url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            return false;
        if (url.Host == "loadspot.amd64fox1.workers.dev")
            return url.AbsolutePath == $"/download/spotify_installer-{version}-x64.exe";
        var prefix = $"/upgrade/client/win32-x86_64/spotify_installer-{version}-";
        return url.Host == "upgrade.scdn.co" && url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            && Regex.IsMatch(url.AbsolutePath[prefix.Length..], @"^[0-9]+\.exe$", RegexOptions.CultureInvariant);
    }

    public static CatalogResult Read(string json, string minimum)
    {
        var minimumVersion = Parse(minimum);
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
            if (number.ToString() != property.Name || number < minimumVersion) continue;
            var build = Text(entry, "buildType");
            if (build is not null && !build.Equals("Release", StringComparison.OrdinalIgnoreCase)) continue;
            var x64 = Child(Child(entry, "win"), "x64");
            var link = x64.ValueKind == JsonValueKind.String ? x64.GetString() : Text(x64, "url");
            link ??= Text(Child(Child(entry, "links"), "win"), "x64");
            if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || !IsCatalogDownload(url, full)) continue;
            var sizeElement = Child(x64, "size");
            long size = sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt64(out var bytes) ? Math.Max(0, bytes) : 0;
            var date = Text(x64, "date");
            if (date is not null && !DateTime.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = null;
            releases.Add(new(full, url, date, size, number == minimumVersion));
        }
        releases.Sort((a, b) => Parse(b.FullVersion!).CompareTo(Parse(a.FullVersion!)));
        SpotifyChoice[] choices = [SpotifyChoice.Latest, .. releases];
        return new(minimum, choices, releases.LastOrDefault() ?? SpotifyChoice.Latest,
            releases.Count == 0 ? $"The catalog has no Windows x64 versions at or above {minimum}. The latest official installer is still available." : null);
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
        var minimum = SpotifyVersions.Parse(Compatibility.TestedVersion).ToString();
        string? failure = null;
        foreach (var (source, seconds) in new[] { (Sources.CatalogApi, 4), (Sources.Catalog, 8) })
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                var result = SpotifyVersions.Read(await downloads.TextAsync(source, deadline.Token), minimum);
                if (result.Choices.Count < 2) throw new InvalidDataException("No usable versions in the catalog.");
                return result;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
            { failure = error.Message; }
        }
        token.ThrowIfCancellationRequested();
        return new(minimum, [Compatibility.TestedChoice, SpotifyChoice.Latest], Compatibility.TestedChoice,
            $"Version list unavailable. The saved link for the tested version is still available. {failure}");
    }
}
