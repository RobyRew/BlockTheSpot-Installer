namespace BlockTheSpot.Core;

public sealed record InstallRequest(SpotifyChoice Choice, bool ReinstallSpotify, bool LaunchSpotify, bool RemoveStoreEdition, bool AllowUntested = false);
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

    public async Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("An installation is already running.");
        var staging = Path.Combine(Path.GetTempPath(), "BlockTheSpot-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            progress.Report(new("Preparing", "Checking the supported Spotify version", 3));
            var config = await downloads.TextAsync(Sources.Config, token);
            var minimum = SpotifyVersions.MinimumFromConfig(config);
            var installed = platform.Inspect();
            // Keeping the installed version is an explicit choice: selecting a catalog
            // entry alone never silently downgrades a working Spotify installation.
            var needsSetup = installed.Version is null || request.ReinstallSpotify;
            if (needsSetup) Compatibility.ValidateChoice(request.Choice, request.AllowUntested);
            else Compatibility.ValidateInstalled(installed.Version!, request.AllowUntested);
            if (!needsSetup) SpotifyVersions.ValidateInstalled(installed.Version!, minimum, null);
            if (needsSetup && request.Choice.FullVersion is { } selected && SpotifyVersions.Parse(selected) < SpotifyVersions.Parse(minimum))
                throw new InvalidOperationException($"This version is older than the supported minimum {minimum}. Refresh the version list.");
            if (await platform.IsStoreInstalledAsync(token) && !request.RemoveStoreEdition)
                throw new InvalidOperationException("Microsoft Store Spotify is installed. Enable 'Replace Microsoft Store edition' to switch to the desktop app.");

            progress.Report(new("Downloading", "Preparing BlockTheSpot files before changing Spotify", 10));
            await downloads.FileAsync(Sources.Chrome, Path.Combine(staging, "chrome_elf.dll"), 0, true, null, token);
            await downloads.FileAsync(Sources.Block, Path.Combine(staging, "blockthespot.dll"), 0, true, null, token);
            await File.WriteAllTextAsync(Path.Combine(staging, "config.ini"), config, token);

            var setup = Path.Combine(staging, "SpotifySetup.exe");
            if (needsSetup)
            {
                var transfer = new InlineProgress<TransferProgress>(p =>
                    progress.Report(new("Downloading Spotify", p.Label, 20 + p.Percent * .4)));
                await downloads.FileAsync(request.Choice.Url, setup, request.Choice.Size, false, transfer, token);
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
            Compatibility.ValidateInstalled(actual, request.AllowUntested);
            platform.ValidateInstalledArchitecture();
            await platform.StopSpotifyAsync(CancellationToken.None);
            progress.Report(new("Applying patch", "Saving original files and applying BlockTheSpot", 85, false));
            // Disk work stays off the UI thread; failed replacements restore the snapshot.
            await Task.Run(() => patch.Apply(platform.SpotifyDirectory, staging, needsSetup));
            if (request.LaunchSpotify) platform.LaunchSpotify();
            progress.Report(new("Completed", $"Spotify {actual} is ready.", 100, false));
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            gate.Release();
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
