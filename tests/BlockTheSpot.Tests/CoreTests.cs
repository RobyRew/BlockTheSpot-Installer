using System.Net;
using System.Text;
using BlockTheSpot.Core;
using Xunit;

namespace BlockTheSpot.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void LiveSchemaSelectsExactScreenshotVersionAndSortsNewestFirst()
    {
        var result = SpotifyVersions.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "loadspot_versions.json")), "1.2.93.667");
        Assert.Equal(4, result.Choices.Count);
        Assert.Equal("1.3.1.223.g6311b0a4", result.Choices[1].FullVersion);
        Assert.Equal("1.2.93.667.g7b5cc0ce", result.Selected.FullVersion);
        Assert.True(result.Selected.Recommended);
        Assert.Equal(146096232, result.Selected.Size);
        Assert.Equal("01.07.2026", result.Selected.Date);
        Assert.Equal("https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe", result.Selected.Url.AbsoluteUri);
        Assert.Equal(SpotifyChoice.Latest, result.Choices[0]);
    }

    [Fact]
    public void StaleCatalogKeepsExplicitLatestOption()
    {
        var result = SpotifyVersions.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "loadspot_versions.json")), "1.9.0.0");
        Assert.Single(result.Choices);
        Assert.NotNull(result.Warning);
        Assert.Equal(SpotifyChoice.Latest, result.Selected);
    }

    [Theory]
    [InlineData("http://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe")]
    [InlineData("https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-arm64.exe")]
    [InlineData("https://loadspot.amd64fox1.workers.dev.evil.test/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe")]
    [InlineData("https://user@loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe")]
    public void RejectsWrongArchitectureAndUnexpectedDownloadHosts(string url) =>
        Assert.False(SpotifyVersions.IsCatalogDownload(new(url), "1.2.93.667.g7b5cc0ce"));

    [Fact]
    public void ReadsLegacyUrlsWithoutLosingInstallerBuildSuffix()
    {
        var result = SpotifyVersions.Read("""{"1.2.85.519":{"buildType":"Release","fullversion":"1.2.85.519.g549a528b","links":{"win":{"x64":"https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-1.2.85.519.g549a528b-4062.exe"}}}}""", "1.2.85.500");
        Assert.EndsWith("-4062.exe", result.Selected.Url.AbsoluteUri);
        Assert.False(result.Selected.Recommended);
    }

    [Theory]
    [InlineData("<html>error</html>")]
    [InlineData("null")]
    public async Task InvalidCatalogFallsBackWithoutLosingLatest(string catalog)
    {
        using var client = new HttpClient(new FakeHandler(uri => Response.Text(uri == Sources.Config ? ";1.2.93.667" : catalog)));
        var result = await new CatalogService(new Downloads(client)).LoadAsync(CancellationToken.None);
        Assert.NotNull(result.Warning);
        Assert.Equal(Compatibility.TestedChoice, result.Selected);
    }

    [Fact]
    public void NumericVersionComparisonAndConfigParsing()
    {
        Assert.True(SpotifyVersions.Parse("1.2.100.1") > SpotifyVersions.Parse("1.2.99.999"));
        Assert.Equal("1.3.1.223", SpotifyVersions.MinimumFromConfig(";Spotify for Windows\r\n;1.3.1.223\r\n[Log]\r\n"));
        Assert.Throws<InvalidDataException>(() => SpotifyVersions.MinimumFromConfig(";not a version"));
        Assert.Throws<InvalidOperationException>(() => SpotifyVersions.ValidateInstalled("1.2.92.1", "1.2.93.667", null));
        Assert.Throws<InvalidOperationException>(() => SpotifyVersions.ValidateInstalled("1.3.1.223", "1.2.93.667", new("1.2.93.667", Sources.LatestSpotify)));
        SpotifyVersions.ValidateInstalled("1.3.1.223", "1.2.93.667", null);
    }

    [Fact]
    public async Task PagesFeedLoadsWithoutDependingOnOtherServers()
    {
        var handler = new FakeHandler(uri => uri == Sources.CatalogApi
            ? Response.Text(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "loadspot_versions.json")))
            : throw new InvalidOperationException("Unexpected upstream request"));
        using var client = new HttpClient(handler);
        var result = await new CatalogService(new Downloads(client)).LoadAsync(CancellationToken.None);
        Assert.Null(result.Warning);
        Assert.Equal(Compatibility.TestedVersion, result.Selected.FullVersion);
        Assert.Equal([Sources.CatalogApi], handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"1.2.93.667\":{\"fullversion\":\"1.99999999999999.3.4.gabcdefab\"}}")]
    public async Task BrokenPagesFeedFallsBackToMaintainedUpstream(string bad)
    {
        var handler = new FakeHandler(uri => Response.Text(uri == Sources.CatalogApi ? bad :
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "loadspot_versions.json"))));
        using var client = new HttpClient(handler);
        var result = await new CatalogService(new Downloads(client)).LoadAsync(CancellationToken.None);
        Assert.Null(result.Warning);
        Assert.Equal(Compatibility.TestedVersion, result.Selected.FullVersion);
        Assert.Equal([Sources.CatalogApi, Sources.Catalog], handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationDoesNotStartAnUpstreamFallback()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var client = new HttpClient(new FakeHandler(_ => throw new OperationCanceledException(token.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CatalogService(new Downloads(client)).LoadAsync(token.Token));
    }
}

public sealed class DownloadTests
{
    [Fact]
    public async Task StreamsAndValidatesExecutableThenReplacesTarget()
    {
        using var directory = new TemporaryDirectory();
        var payload = Response.Executable();
        using var client = new HttpClient(new FakeHandler(_ => Response.Binary(payload)));
        var target = Path.Combine(directory.Path, "setup.exe");
        await new Downloads(client).FileAsync(Sources.LatestSpotify, target, payload.Length, false, null, CancellationToken.None);
        Assert.Equal(payload, File.ReadAllBytes(target));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(410)]
    public async Task MissingPinnedDownloadsReturnUsefulErrorWithoutSubstitution(int status)
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHandler(_ => new((HttpStatusCode)status));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new Downloads(client).FileAsync(Sources.LatestSpotify, Path.Combine(directory.Path, "setup.exe"), 0, false, null, CancellationToken.None));
        Assert.Contains("Refresh versions", error.Message);
        Assert.Single(handler.Requests);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("html")]
    [InlineData("empty")]
    [InlineData("size")]
    [InlineData("architecture")]
    public async Task InvalidPayloadPreservesExistingTargetAndCleansTemporaryFiles(string kind)
    {
        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "setup.exe");
        await File.WriteAllTextAsync(target, "original");
        var payload = kind switch { "html" => Encoding.UTF8.GetBytes("<html>error</html>"), "empty" => [], _ => Response.Executable(x64: kind != "architecture") };
        using var client = new HttpClient(new FakeHandler(_ => Response.Binary(payload)));
        await Assert.ThrowsAnyAsync<Exception>(() => new Downloads(client).FileAsync(Sources.LatestSpotify, target, kind == "size" ? 99999 : 0, kind == "architecture", null, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task CancellationNeverReplacesAnExistingFile()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new FakeHandler(_ => { cancellation.Cancel(); return Response.Binary(Response.Executable()); }));
        var target = Path.Combine(directory.Path, "setup.exe");
        File.WriteAllText(target, "original");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Downloads(client).FileAsync(Sources.LatestSpotify, target, 0, false, null, cancellation.Token));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(directory.Path));
    }
}

public sealed class TransactionTests
{
    [Fact]
    public void PatchAndRestorePreserveOriginalDll()
    {
        using var directory = new TemporaryDirectory();
        var spotify = directory.Create("spotify");
        var staged = Stage(directory);
        File.WriteAllText(Path.Combine(spotify, "chrome_elf.dll"), "original");
        var transaction = new PatchTransaction();
        transaction.Apply(spotify, staged, false);
        Assert.Equal("original", File.ReadAllText(Path.Combine(spotify, "chrome_elf_required.dll")));
        transaction.Apply(spotify, staged, false);
        transaction.Restore(spotify);
        Assert.Equal("original", File.ReadAllText(Path.Combine(spotify, "chrome_elf.dll")));
        Assert.False(File.Exists(Path.Combine(spotify, "blockthespot.dll")));
    }

    [Fact]
    public void FailedPatchRestoresEveryPreviousFile()
    {
        using var directory = new TemporaryDirectory();
        var spotify = directory.Create("spotify");
        File.WriteAllText(Path.Combine(spotify, "chrome_elf.dll"), "original");
        File.WriteAllText(Path.Combine(spotify, "config.ini"), "user settings");
        var transaction = new PatchTransaction(new FailingFiles());
        Assert.Throws<IOException>(() => transaction.Apply(spotify, Stage(directory), false));
        Assert.Equal("original", File.ReadAllText(Path.Combine(spotify, "chrome_elf.dll")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(spotify, "config.ini")));
        Assert.False(File.Exists(Path.Combine(spotify, "chrome_elf_required.dll")));
        Assert.False(File.Exists(Path.Combine(spotify, "blockthespot.dll")));
    }

    [Fact]
    public void ReinstallRefreshesBackupInsteadOfKeepingPreviousVersion()
    {
        using var directory = new TemporaryDirectory();
        var spotify = directory.Create("spotify");
        File.WriteAllText(Path.Combine(spotify, "chrome_elf.dll"), "new Spotify original");
        File.WriteAllText(Path.Combine(spotify, "chrome_elf_required.dll"), "stale original");
        new PatchTransaction().Apply(spotify, Stage(directory), true);
        Assert.Equal("new Spotify original", File.ReadAllText(Path.Combine(spotify, "chrome_elf_required.dll")));
    }

    [Fact]
    public void MissingStagedFileDoesNotTouchSpotify()
    {
        using var directory = new TemporaryDirectory();
        var spotify = directory.Create("spotify");
        File.WriteAllText(Path.Combine(spotify, "chrome_elf.dll"), "original");
        Assert.Throws<FileNotFoundException>(() => new PatchTransaction().Apply(spotify, directory.Create("empty"), false));
        Assert.Equal("original", File.ReadAllText(Path.Combine(spotify, "chrome_elf.dll")));
    }

    private static string Stage(TemporaryDirectory directory)
    {
        var stage = directory.Create("stage");
        foreach (var file in new[] { "chrome_elf.dll", "blockthespot.dll", "config.ini" })
            File.WriteAllText(Path.Combine(stage, file), "patch " + file);
        return stage;
    }

    private sealed class FailingFiles : IFileOperations
    {
        private int count;
        public void Copy(string source, string destination)
        { if (++count == 3) throw new IOException("Simulated disk failure"); File.Copy(source, destination, true); }
        public void Delete(string path) => File.Delete(path);
    }
}

public sealed class InstallerTests
{
    [Fact]
    public async Task KeepingCurrentVersionDoesNotForceADowngrade()
    {
        using var directory = new TemporaryDirectory();
        var platform = new FakePlatform(directory.Path) { Version = "1.3.1.223" };
        File.WriteAllText(Path.Combine(directory.Path, "chrome_elf.dll"), "original");
        using var client = Client();
        await new InstallerService(new Downloads(client), platform).InstallAsync(
            new(new("1.2.93.667", Sources.LatestSpotify), false, false, false, true), new InlineProgress<InstallProgress>(_ => { }), CancellationToken.None);
        Assert.DoesNotContain("setup", platform.Events);
        Assert.True(File.Exists(Path.Combine(directory.Path, "blockthespot.dll")));
    }

    [Fact]
    public async Task FailedDownloadLeavesSpotifyRunningAndUntouched()
    {
        using var directory = new TemporaryDirectory();
        var platform = new FakePlatform(directory.Path);
        using var client = Client(failPatch: true);
        await Assert.ThrowsAsync<HttpRequestException>(() => new InstallerService(new Downloads(client), platform).InstallAsync(
            new(Compatibility.TestedChoice with { Size = 0 }, true, false, false), new InlineProgress<InstallProgress>(_ => { }), CancellationToken.None));
        Assert.DoesNotContain("stop", platform.Events);
        Assert.DoesNotContain("setup", platform.Events);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SignatureIsVerifiedBeforeSpotifyIsStoppedOrSetupRuns()
    {
        using var directory = new TemporaryDirectory();
        var platform = new FakePlatform(directory.Path);
        using var client = Client();
        await new InstallerService(new Downloads(client), platform).InstallAsync(
            new(Compatibility.TestedChoice with { Size = 0 }, true, true, false), new InlineProgress<InstallProgress>(_ => { }), CancellationToken.None);
        Assert.True(platform.Events.IndexOf("signature") < platform.Events.IndexOf("stop"));
        Assert.True(platform.Events.IndexOf("signature") < platform.Events.IndexOf("setup"));
        Assert.Contains("launch", platform.Events);
    }

    [Fact]
    public async Task StoreRemovalRequiresExplicitOption()
    {
        using var directory = new TemporaryDirectory();
        var platform = new FakePlatform(directory.Path) { StoreInstalled = true };
        using var client = Client();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InstallerService(new Downloads(client), platform).InstallAsync(
            new(Compatibility.TestedChoice with { Size = 0 }, true, false, false), new InlineProgress<InstallProgress>(_ => { }), CancellationToken.None));
        Assert.DoesNotContain("remove-store", platform.Events);
        Assert.DoesNotContain("stop", platform.Events);
    }

    private static HttpClient Client(bool failPatch = false) => new(new FakeHandler(uri =>
        uri == Sources.Config ? Response.Text(";Spotify for Windows\n;1.2.93.667\n[Log]\nLevel=0") :
        failPatch ? new(HttpStatusCode.NotFound) : Response.Binary(Response.Executable())));

    private sealed class FakePlatform(string directory) : ISpotifyPlatform
    {
        public string SpotifyDirectory => directory;
        public string? Version { get; set; }
        public bool StoreInstalled { get; set; }
        public List<string> Events { get; } = [];
        public InstalledSpotify Inspect() => new(Version, false);
        public void ValidateInstalledArchitecture() { }
        public Task<bool> IsStoreInstalledAsync(CancellationToken token) => Task.FromResult(StoreInstalled);
        public Task RemoveStoreAsync(CancellationToken token) { Events.Add("remove-store"); return Task.CompletedTask; }
        public Task StopSpotifyAsync(CancellationToken token) { Events.Add("stop"); return Task.CompletedTask; }
        public Task VerifySpotifyPublisherAsync(string path, CancellationToken token) { Events.Add("signature"); return Task.CompletedTask; }
        public Task RunSetupAsync(string path, string minimum, SpotifyChoice selected, CancellationToken token)
        {
            Events.Add("setup"); Version = selected.FullVersion ?? "1.3.1.223";
            File.WriteAllText(Path.Combine(directory, "chrome_elf.dll"), "original"); return Task.CompletedTask;
        }
        public void LaunchSpotify() => Events.Add("launch");
    }
}

internal sealed class FakeHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Requests.Add(request.RequestUri!); return Task.FromResult(respond(request.RequestUri!)); }
}

internal static class Response
{
    public static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    public static HttpResponseMessage Binary(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    public static byte[] Executable(bool x64 = true)
    {
        var bytes = new byte[512];
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        writer.Write((ushort)0x5A4D);
        writer.BaseStream.Position = 0x3C; writer.Write(0x80);
        writer.BaseStream.Position = 0x80; writer.Write(0x00004550);
        writer.Write((ushort)(x64 ? 0x8664 : 0x14c));
        writer.Write((ushort)0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write((ushort)240); writer.Write((ushort)0x2022);
        writer.Write((ushort)0x20B);
        return bytes;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BlockTheSpot.Tests-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public string Create(string name) { var path = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(path); return path; }
    public void Dispose() => Directory.Delete(Path, true);
}
