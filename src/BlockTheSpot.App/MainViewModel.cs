using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using BlockTheSpot.Core;
using Microsoft.Win32;

namespace BlockTheSpot.App;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HttpClient http = Downloads.CreateClient();
    private readonly WindowsSpotifyPlatform platform = new();
    private readonly CatalogService catalog;
    private readonly InstallerService installer;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private readonly Queue<string> log = new();
    private bool busy, loading, canCancel, reinstall = true, launch = true, removeStore, showLog, allowUntested;
    private IReadOnlyList<SpotifyChoice> catalogChoices = [];
    private string status = "Getting ready", detail = "Loading Spotify versions…", installedLabel = "Checking your installation…", installedDetail = "";
    private string hint = "Loading the live LoadSpot catalog", versionLabel = "v" + AppVersion, lastStage = "";
    private SpotifyChoice? selected;
    private double progressValue;

    public static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
    public ObservableCollection<SpotifyChoice> Choices { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand RefreshCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ReleasesCommand { get; }
    public RelayCommand SaveLogCommand { get; }
    public bool CanEdit => !busy && !loading;
    public bool IsBusy => busy;
    public bool IsLoading => loading;
    public bool CanCancel => busy && canCancel;
    public bool ReinstallSpotify { get => reinstall; set => Set(ref reinstall, value); }
    public bool LaunchSpotify { get => launch; set => Set(ref launch, value); }
    public bool RemoveStoreEdition { get => removeStore; set => Set(ref removeStore, value); }
    public bool ShowLog { get => showLog; set => Set(ref showLog, value); }
    public bool AllowUntested { get => allowUntested; set { Set(ref allowUntested, value); PopulateChoices(); } }
    public SpotifyChoice? SelectedChoice { get => selected; set => Set(ref selected, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string StatusDetail { get => detail; private set => Set(ref detail, value); }
    public string InstalledLabel { get => installedLabel; private set => Set(ref installedLabel, value); }
    public string InstalledDetail { get => installedDetail; private set => Set(ref installedDetail, value); }
    public string CatalogHint { get => hint; private set => Set(ref hint, value); }
    public string VersionLabel { get => versionLabel; private set => Set(ref versionLabel, value); }
    public double ProgressValue { get => progressValue; private set { Set(ref progressValue, value); Notify(nameof(ProgressLabel)); } }
    public string ProgressLabel => busy ? $"{ProgressValue:0}%" : "";
    public string LogText => string.Join(Environment.NewLine, log);

    public MainViewModel(bool preview)
    {
        var downloads = new Downloads(http);
        catalog = new(downloads);
        installer = new(downloads, platform);
        RefreshCommand = new(() => ObserveAsync(RefreshAsync), () => CanEdit);
        InstallCommand = new(() => ObserveAsync(() => RunAsync(false)), () => CanEdit && SelectedChoice is not null);
        RestoreCommand = new(() => ObserveAsync(() => RunAsync(true)), () => CanEdit);
        CancelCommand = new(Cancel, () => CanCancel);
        ReleasesCommand = new(() => OpenUrl(Sources.Repository + "/releases/latest"));
        SaveLogCommand = new(SaveLog);
        if (preview)
        {
            catalogChoices = [Compatibility.TestedChoice, SpotifyChoice.Latest];
            PopulateChoices();
            SelectedChoice = Choices[0];
            InstalledLabel = "Spotify 1.2.93.667";
            InstalledDetail = "Desktop edition · Ready to install BlockTheSpot";
            CatalogHint = "Pinned to the latest tested compatible version. Newer builds are available in Advanced options.";
            Status = "Ready when you are";
            StatusDetail = "Spotify 1.2.93.667 will be installed before patching.";
        }
    }

    public async Task InitializeAsync()
    {
        Inspect();
        await Task.WhenAll(RefreshAsync(), CheckUpdateAsync());
    }

    private void Inspect()
    {
        var current = platform.Inspect();
        InstalledLabel = current.Version is null ? "Spotify is not installed" : "Spotify " + current.Version;
        InstalledDetail = current.Version is null ? "We'll install the selected desktop version for you." :
            current.Patched ? "Desktop edition · BlockTheSpot installed" : "Desktop edition · Ready to patch";
    }

    private async Task RefreshAsync()
    {
        loading = true; ChangedState();
        try
        {
            var result = await catalog.LoadAsync(lifetime.Token);
            catalogChoices = result.Choices;
            PopulateChoices();
            CatalogHint = result.Warning ?? $"Tested: {Compatibility.TestedVersion} · Newer builds require Advanced options.";
            Status = result.Warning is null ? "Ready when you are" : "Version list needs attention";
            StatusDetail = result.Warning ?? "Choose your options, then install BlockTheSpot.";
            AddLog(result.Warning ?? $"Loaded {result.Choices.Count - 1} Spotify versions from LoadSpot.");
        }
        finally { loading = false; ChangedState(); }
    }

    private async Task RunAsync(bool restore)
    {
        if (WindowsSpotifyPlatform.IsAdministrator)
            throw new InvalidOperationException("Open BlockTheSpot normally, without 'Run as administrator'. Spotify installs for your Windows account.");
        busy = true; canCancel = true; lastStage = ""; ProgressValue = 0;
        ChangedState();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cancellation;
        var updates = new Progress<InstallProgress>(update =>
        {
            Status = update.Stage; StatusDetail = update.Detail; ProgressValue = update.Percent;
            canCancel = update.CanCancel; ChangedState();
            if (lastStage != update.Stage) { AddLog($"{update.Stage}: {update.Detail}"); lastStage = update.Stage; }
        });
        try
        {
            if (restore) await installer.RestoreAsync(updates, cancellation.Token);
            else await installer.InstallAsync(new(SelectedChoice!, ReinstallSpotify, LaunchSpotify, RemoveStoreEdition, AllowUntested), updates, cancellation.Token);
            Inspect();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Cancelled"; StatusDetail = "Preparation stopped. No patch was applied."; AddLog(StatusDetail);
        }
        finally { operation = null; busy = false; canCancel = false; ChangedState(); }
    }

    public void Cancel()
    {
        if (!CanCancel) return;
        canCancel = false; operation?.Cancel();
        StatusDetail = "Cancelling the download…"; ChangedState();
    }

    private void PopulateChoices()
    {
        var previous = SelectedChoice;
        var tested = catalogChoices.FirstOrDefault(c => c.FullVersion == Compatibility.TestedVersion) ?? Compatibility.TestedChoice;
        Choices.Clear();
        Choices.Add(tested with { Recommended = true });
        if (AllowUntested)
            foreach (var choice in catalogChoices.Where(c => c.FullVersion != Compatibility.TestedVersion))
                Choices.Add(choice with { Recommended = false });
        SelectedChoice = Choices.FirstOrDefault(c => c.FullVersion == previous?.FullVersion) ?? Choices[0];
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            var json = await new Downloads(http).TextAsync(Sources.LatestRelease, lifetime.Token);
            using var release = JsonDocument.Parse(json);
            var tag = release.RootElement.GetProperty("tag_name").GetString();
            if (Version.TryParse(tag?.TrimStart('v'), out var latest) && Version.TryParse(AppVersion, out var current) && latest > current)
                VersionLabel = $"v{AppVersion} · Update available";
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or OperationCanceledException)
        { AddLog("Release check unavailable. Releases can still be opened from the version button."); }
    }

    private async void ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Status = "Couldn't finish"; StatusDetail = error.Message;
            AddLog(error.ToString()); ShowLog = true;
        }
    }

    private void SaveLog()
    {
        var dialog = new SaveFileDialog { FileName = $"BlockTheSpot-{DateTime.Now:yyyyMMdd-HHmm}.log", Filter = "Log file (*.log)|*.log" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, LogText); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { StatusDetail = $"Couldn't save the log: {error.Message}"; }
    }

    private void AddLog(string text)
    {
        log.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text}");
        while (log.Count > 200) log.Dequeue();
        Notify(nameof(LogText));
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show("Your browser could not be opened. Visit " + url, "Open link"); }
    }

    private void ChangedState()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(CanEdit), nameof(IsLoading), nameof(CanCancel), nameof(ProgressLabel) }) Notify(name);
        CommandManager.InvalidateRequerySuggested();
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Notify(name); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); http.Dispose(); }
}

public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
