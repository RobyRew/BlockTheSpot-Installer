namespace BlockTheSpot.Core;

public static class Compatibility
{
    // Change deliberately after testing a newer Spotify version with BlockTheSpot.
    // An upstream config or catalog update must never move this default.
    public const string TestedVersion = "1.2.93.667.g7b5cc0ce";
    // SHA-256 of the mirror file as downloaded on 2026-09-18: ProductVersion 1.2.93.667.g7b5cc0ce,
    // Authenticode signer O=Spotify AB. Pins the exact bytes that were tested, on top of the signature check.
    public static SpotifyChoice TestedChoice { get; } = new(TestedVersion,
        new("https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe"),
        "01.07.2026", 146096232, true, Sha256: "614741858a7def3fb21da385312fe7b114dd2873f6d79fb3d980a979af560ba0");

    public static void ValidateChoice(SpotifyChoice choice, bool allowUntested)
    {
        if (!allowUntested && !string.Equals(choice.FullVersion, TestedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The tested Spotify version is {TestedVersion}. Tick 'All versions' to choose a different build.");
    }

    public static void ValidateInstalled(string installed, bool allowUntested)
    {
        if (!allowUntested && SpotifyVersions.Parse(installed) != SpotifyVersions.Parse(TestedVersion))
            throw new InvalidOperationException($"Spotify {installed} has not been verified with this release. Enable 'Install this Spotify version' to install {TestedVersion}, or tick 'All versions' to keep it.");
    }
}
