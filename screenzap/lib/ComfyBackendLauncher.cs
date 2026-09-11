using System.Diagnostics;
using System.Net.Http;

namespace screenzap.lib;

internal static class ComfyBackendLauncher
{
    // Serialize starts from multiple editor windows. An existing server is reused.
    private static readonly SemaphoreSlim startup = new(1, 1);

    internal static async Task EnsureAvailableAsync(HttpClient client, IProgress<string>? progress, CancellationToken cancellation)
    {
        if (await IsAvailableAsync(client, cancellation)) return;
        if (client.BaseAddress?.Port != 8188)
            throw new InvalidOperationException("Start ComfyUI at the selected local server address.");
        await startup.WaitAsync(cancellation);
        try
        {
            if (await IsAvailableAsync(client, cancellation)) return;
            progress?.Report("Starting local image backend…");
            var start = new ProcessStartInfo("powershell.exe")
            {
                // Shell launch detaches the server from the editor/test runner's handles.
                // Waiting on inherited redirected pipes would wait for the server to exit.
                UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
                         Path.Combine(AppContext.BaseDirectory, "Workflows", "Start-ImageBackend.ps1") })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local backend.");
            using var launchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            launchTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(launchTimeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new TimeoutException("Local backend launcher timed out. Check the backend logs."); }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Could not start the local backend. Check %LOCALAPPDATA%\\Screenzap\\ComfyUI\\launcher-error.log.");
            for (int attempt = 0; attempt < 90; attempt++)
            {
                if (await IsAvailableAsync(client, cancellation)) return;
                await Task.Delay(1000, cancellation);
            }
            throw new InvalidOperationException("Local backend did not start. Check %LOCALAPPDATA%\\Screenzap\\ComfyUI\\server-error.log.");
        }
        finally { startup.Release(); }
    }

    private static async Task<bool> IsAvailableAsync(HttpClient client, CancellationToken cancellation)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        probe.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await client.GetAsync("system_stats", probe.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return false; }
    }
}
