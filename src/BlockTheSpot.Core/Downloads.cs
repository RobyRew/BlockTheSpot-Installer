using System.Buffers;
using System.Net;
using System.Reflection.PortableExecutable;

namespace BlockTheSpot.Core;

public sealed record TransferProgress(long Bytes, long? Total)
{
    public double Percent => Total > 0 ? Math.Min(100, 100d * Bytes / Total.Value) : 0;
    public string Label => Total > 0 ? $"{Bytes / 1048576d:F1} / {Total / 1048576d:F1} MiB" : $"{Bytes / 1048576d:F1} MiB";
}

public sealed class Downloads(HttpClient client)
{
    public static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestHeaders = { { "User-Agent", "BlockTheSpotInstaller" } } };

    public async Task<string> TextAsync(Uri uri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendAsync(uri, timeout.Token);
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token);
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    public async Task FileAsync(Uri uri, string target, long expectedSize, bool requireX64Dll,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var cancellation = timeout.Token;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await SendAsync(uri, cancellation);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Expected a complete file from {uri.Host}; received HTTP {(int)response.StatusCode}.");
            var total = response.Content.Headers.ContentLength;
            if (expectedSize > 0 && total > 0 && total != expectedSize)
                throw new InvalidDataException("The download size has changed. Refresh versions and try again.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(131072);
                try
                {
                    long written = 0;
                    var lastReport = Environment.TickCount64;
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                        written += count;
                        if (Environment.TickCount64 - lastReport > 100)
                        {
                            progress?.Report(new(written, total ?? (expectedSize > 0 ? expectedSize : null)));
                            lastReport = Environment.TickCount64;
                        }
                    }
                    if (written == 0 || (expectedSize > 0 && written != expectedSize) || (total.HasValue && written != total))
                        throw new InvalidDataException("The download is incomplete. Please try again.");
                    progress?.Report(new(written, total));
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            ValidatePortableExecutable(temporary, requireX64Dll);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void ValidatePortableExecutable(string path, bool requireX64Dll)
    {
        using var stream = File.OpenRead(path);
        using var executable = new PEReader(stream);
        if (executable.PEHeaders.PEHeader is null ||
            !executable.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.ExecutableImage))
            throw new InvalidDataException("The downloaded file is not a Windows executable.");
        if (requireX64Dll && (executable.PEHeaders.CoffHeader.Machine != Machine.Amd64 ||
            !executable.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)))
            throw new InvalidDataException("The downloaded patch is not a Windows x64 DLL.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try { response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
                continue;
            }
            if (((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) && attempt < 2)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode;
                response.Dispose();
                var action = code is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden
                    ? " Refresh versions or choose the latest official Spotify installer." : " Please try again.";
                throw new HttpRequestException($"Download unavailable from {uri.Host} (HTTP {(int)code}).{action}", null, code);
            }
            return response;
        }
    }
}
