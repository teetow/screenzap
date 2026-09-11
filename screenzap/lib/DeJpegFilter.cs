using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace screenzap.lib;

internal interface IDeJpegFilter
{
    Task<byte[]> CleanAsync(byte[] png, IProgress<string>? progress, CancellationToken cancellation);
}

internal sealed class ComfyDeJpegFilter : IDeJpegFilter
{
    private readonly HttpClient client;
    private readonly string workflow;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan timeout;

    public ComfyDeJpegFilter(HttpClient client, string workflow,
        TimeSpan? pollInterval = null, TimeSpan? timeout = null)
    {
        this.client = client;
        this.workflow = workflow;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        this.timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    public static Uri ParseEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme != "http" || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use a local ComfyUI address such as http://127.0.0.1:8188.");
        return uri;
    }

    public async Task<byte[]> CleanAsync(byte[] png, IProgress<string>? progress, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            var graph = JsonNode.Parse(workflow)?.AsObject() ?? throw new InvalidOperationException("Invalid workflow JSON.");
            progress?.Report("Preparing image…");
            using var upload = new MultipartFormDataContent();
            using var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            upload.Add(file, "image", $"screenzap-{Guid.NewGuid():N}.png");
            upload.Add(new StringContent("input"), "type");
            var uploaded = await SendJsonAsync(HttpMethod.Post, "upload/image", upload, token).ConfigureAwait(false);
            var name = uploaded["name"]?.GetValue<string>() ?? throw new InvalidOperationException("ComfyUI did not return an uploaded filename.");
            var folder = uploaded["subfolder"]?.GetValue<string>();
            graph["1"]!["inputs"]!["image"] = string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
            graph["9"]!["inputs"]!["filename_prefix"] = $"Screenzap/{Guid.NewGuid():N}";
            progress?.Report("Removing JPEG artifacts…");
            using var payload = new StringContent(new JsonObject { ["prompt"] = graph }.ToJsonString(), Encoding.UTF8, "application/json");
            var submitted = await SendJsonAsync(HttpMethod.Post, "prompt", payload, token).ConfigureAwait(false);
            var id = submitted["prompt_id"]?.GetValue<string>() ?? throw new InvalidOperationException("ComfyUI rejected the workflow: " + submitted.ToJsonString());
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var history = await SendJsonAsync(HttpMethod.Get, "history/" + Uri.EscapeDataString(id), null, token).ConfigureAwait(false);
                if (history[id] is JsonObject job)
                {
                    var status = job["status"];
                    if (status?["status_str"]?.GetValue<string>() == "error")
                        throw new InvalidOperationException("JPEG cleanup failed: " + status.ToJsonString());
                    if (status?["completed"]?.GetValue<bool>() == true)
                    {
                        var output = job["outputs"]?["9"]?["images"]?[0];
                        if (output == null) throw new InvalidOperationException("ComfyUI completed without an output image.");
                        string Field(string key) => Uri.EscapeDataString(output[key]?.GetValue<string>() ?? "");
                        progress?.Report("Finishing cleanup…");
                        return await client.GetByteArrayAsync($"view?filename={Field("filename")}&subfolder={Field("subfolder")}&type={Field("type")}", token).ConfigureAwait(false);
                    }
                }
                await Task.Delay(pollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("ComfyUI timed out. Check its queue, model setup, and available GPU memory.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Cannot reach local ComfyUI. Start it and check the server address. " + ex.Message, ex);
        }
        // Cancellation stops this client only. /interrupt could kill another client's job.
    }

    private async Task<JsonObject> SendJsonAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI {path} failed ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 2000)]}");
        return JsonNode.Parse(body)?.AsObject() ?? throw new InvalidOperationException("ComfyUI returned invalid JSON.");
    }
}
