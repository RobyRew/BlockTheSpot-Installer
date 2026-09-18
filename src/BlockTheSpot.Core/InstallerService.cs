namespace BlockTheSpot.Core;

public sealed record InstallRequest(SpotifyChoice Choice, bool ReinstallSpotify, bool LaunchSpotify, bool RemoveStoreEdition, bool AllowUntested = false, bool ApplyPatch = true);
public sealed record InstalledSpotify(string? Version, bool Patched);
public sealed record InstallProgress(string Stage, string Detail, double Percent, bool CanCancel = true);

public interface ISpotifyPlatform
{
    string SpotifyDirectory { get; }
    InstalledSpotify Inspect();
    void ValidateInstalledArchitecture();
    Task<bool> IsStoreInstalledAsync(CancellationToken token);
    Task RemoveStoreAsync(CancellationToken token);
    Task StopSpotifyAsync(CancellationToken token);
    Task VerifySpotifyPublisherAsync(string installerPath, CancellationToken token);
    Task RunSetupAsync(string installerPath, string minimum, SpotifyChoice selected, CancellationToken token);
    void LaunchSpotify();
}

public sealed class InstallerService(Downloads downloads, ISpotifyPlatform platform, PatchTransaction? transaction = null)
{
    private readonly PatchTransaction patch = transaction ?? new PatchTransaction();
    private readonly SemaphoreSlim gate = new(1, 1);
    // Any version is accepted when only Spotify is installed; the patch has its own minimum.
    private const string NoMinimum = "1.0.0.0";

    public async Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("An installation is already running.");
        var staging = Path.Combine(Path.GetTempPath(), "BlockTheSpot-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var installed = platform.Inspect();
            // Keeping the installed version is an explicit choice: selecting a catalog
            // entry alone never silently downgrades a working Spotify installation.
            var needsSetup = installed.Version is null || request.ReinstallSpotify;
            if (!needsSetup && !request.ApplyPatch)
                throw new InvalidOperationException("Nothing to do: enable 'Install this Spotify version' or 'Apply BlockTheSpot patch'.");
            var minimum = NoMinimum;
            string? config = null;
            if (request.ApplyPatch)
            {
                progress.Report(new("Preparing", "Checking the supported Spotify version", 3));
                config = await downloads.TextAsync(Sources.Config, token);
                minimum = SpotifyVersions.MinimumFromConfig(config);
                if (needsSetup) Compatibility.ValidateChoice(request.Choice, request.AllowUntested);
                else Compatibility.ValidateInstalled(installed.Version!, request.AllowUntested);
                if (!needsSetup) SpotifyVersions.ValidateInstalled(installed.Version!, minimum, null);
                if (needsSetup && request.Choice.FullVersion is { } selected && SpotifyVersions.Parse(selected) < SpotifyVersions.Parse(minimum))
                    throw new InvalidOperationException($"This version is older than the supported minimum {minimum}. Choose a newer version, or turn off the patch to install only Spotify.");
            }
            if (await platform.IsStoreInstalledAsync(token) && !request.RemoveStoreEdition)
                throw new InvalidOperationException("Microsoft Store Spotify is installed. Enable 'Replace Microsoft Store edition' to switch to the desktop app.");

            if (request.ApplyPatch)
            {
                progress.Report(new("Downloading", "Preparing BlockTheSpot files before changing Spotify", 10));
                await downloads.FileAsync(Sources.Chrome, Path.Combine(staging, "chrome_elf.dll"), 0, true, null, token);
                await downloads.FileAsync(Sources.Block, Path.Combine(staging, "blockthespot.dll"), 0, true, null, token);
                await File.WriteAllTextAsync(Path.Combine(staging, "config.ini"), config!, token);
            }

            var setup = Path.Combine(staging, "SpotifySetup.exe");
            if (needsSetup)
            {
                await DownloadSpotifyAsync(request.Choice, setup, progress, token);
                progress.Report(new("Verifying", "Checking Spotify's digital signature", 62));
                await platform.VerifySpotifyPublisherAsync(setup, token);
            }
            token.ThrowIfCancellationRequested();
            // Cancellation is intentionally disabled once setup or file replacement starts.
            // Let these short critical operations finish instead of leaving a partial install.
            progress.Report(new("Installing", "Downloads verified. Finishing safely…", 65, false));
            await platform.StopSpotifyAsync(CancellationToken.None);
            if (request.RemoveStoreEdition && await platform.IsStoreInstalledAsync(CancellationToken.None))
                await platform.RemoveStoreAsync(CancellationToken.None);
            if (needsSetup)
                await platform.RunSetupAsync(setup, minimum, request.Choice, CancellationToken.None);
            var actual = platform.Inspect().Version ?? throw new InvalidOperationException("Spotify setup did not produce a desktop installation.");
            SpotifyVersions.ValidateInstalled(actual, minimum, needsSetup ? request.Choice : null);
            if (request.ApplyPatch) Compatibility.ValidateInstalled(actual, request.AllowUntested);
            platform.ValidateInstalledArchitecture();
            await platform.StopSpotifyAsync(CancellationToken.None);
            if (request.ApplyPatch)
            {
                progress.Report(new("Applying patch", "Saving original files and applying BlockTheSpot", 85, false));
                // Disk work stays off the UI thread; failed replacements restore the snapshot.
                await Task.Run(() => patch.Apply(platform.SpotifyDirectory, staging, needsSetup));
            }
            else
            {
                // Setup wrote a fresh chrome_elf.dll, so leftover patch files and the old backup
                // would only misreport the installation as patched or restore the wrong DLL later.
                progress.Report(new("Cleaning up", "Removing previous BlockTheSpot files", 85, false));
                await Task.Run(() => patch.Discard(platform.SpotifyDirectory));
            }
            if (request.LaunchSpotify) platform.LaunchSpotify();
            progress.Report(new("Completed", request.ApplyPatch ? $"Spotify {actual} is ready." : $"Spotify {actual} installed without the patch.", 100, false));
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            gate.Release();
        }
    }

    // Spotify's own link is tried first (its permanent URL with If-Match, or a versioned link that
    // Spotify has usually expired with HTTP 403), then the CI archive copy, then the LoadSpot mirror.
    // A failed request moves on; a SHA-256, size or executable mismatch does not, because a wrong
    // file is suspicious rather than missing.
    private async Task DownloadSpotifyAsync(SpotifyChoice choice, string target, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var urls = choice.Urls.ToList();
        for (var index = 0; index < urls.Count; index++)
        {
            var url = urls[index];
            var origin = SpotifyChoice.SourceOf(url);
            var transfer = new InlineProgress<TransferProgress>(p =>
                progress.Report(new("Downloading Spotify", $"{p.Label} · {origin}", 20 + p.Percent * .4)));
            progress.Report(new("Downloading Spotify", $"Connecting to {url.Host}", 20));
            try
            {
                await downloads.FileAsync(url, target, choice.Size, false, transfer, token, choice.Sha256, url == Sources.LatestSpotify ? choice.ETag : null);
                return;
            }
            catch (HttpRequestException error) when (index < urls.Count - 1)
            {
                var reason = error.StatusCode is { } code ? $"HTTP {(int)code}" : "connection failed";
                progress.Report(new("Switching source", $"{origin} did not serve this version ({reason}). Trying {SpotifyChoice.SourceOf(urls[index + 1])}.", 20));
            }
        }
    }

    public async Task RestoreAsync(IProgress<InstallProgress> progress, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("An installation is already running.");
        try
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new("Restoring", "Restoring Spotify's original files", 30, false));
            await platform.StopSpotifyAsync(CancellationToken.None);
            await Task.Run(() => patch.Restore(platform.SpotifyDirectory));
            progress.Report(new("Completed", "Original Spotify files restored.", 100, false));
        }
        finally { gate.Release(); }
    }
}

public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
