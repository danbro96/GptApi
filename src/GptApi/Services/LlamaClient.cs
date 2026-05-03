using System.Net.Http.Headers;
using System.Text;

namespace GptApi.Services;

/// <summary>
/// Thin proxy over <c>llama.cpp</c>'s built-in OpenAI-compatible HTTP server
/// (<c>llama-server</c>). Exposes both buffered and streaming completion paths.
/// </summary>
public sealed class LlamaClient
{
    static readonly MediaTypeHeaderValue JsonContentType = new("application/json");

    readonly HttpClient _http;

    public LlamaClient(HttpClient http) => _http = http;

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var resp = await _http.GetAsync("/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Buffered chat completion. Returns the full JSON response body as a string.
    /// </summary>
    public async Task<string> ChatCompletionAsync(string requestJson, CancellationToken ct) =>
        await PostJsonBufferedAsync("/v1/chat/completions", requestJson, ct);

    /// <summary>
    /// Buffered text completion (legacy <c>/v1/completions</c>).
    /// </summary>
    public async Task<string> CompletionAsync(string requestJson, CancellationToken ct) =>
        await PostJsonBufferedAsync("/v1/completions", requestJson, ct);

    /// <summary>
    /// Streaming chat completion. Caller owns the returned <see cref="HttpResponseMessage"/>
    /// and is responsible for piping its content stream to the client. Headers are read
    /// eagerly so backpressure can flow end-to-end.
    /// </summary>
    public Task<HttpResponseMessage> StreamChatCompletionAsync(string requestJson, CancellationToken ct) =>
        PostJsonStreamingAsync("/v1/chat/completions", requestJson, ct);

    public Task<HttpResponseMessage> StreamCompletionAsync(string requestJson, CancellationToken ct) =>
        PostJsonStreamingAsync("/v1/completions", requestJson, ct);

    async Task<string> PostJsonBufferedAsync(string path, string requestJson, CancellationToken ct)
    {
        using var content = new StringContent(requestJson, Encoding.UTF8);
        content.Headers.ContentType = JsonContentType;
        using var resp = await _http.PostAsync(path, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(body, inner: null, statusCode: resp.StatusCode);
        return body;
    }

    async Task<HttpResponseMessage> PostJsonStreamingAsync(string path, string requestJson, CancellationToken ct)
    {
        var content = new StringContent(requestJson, Encoding.UTF8);
        content.Headers.ContentType = JsonContentType;

        using var msg = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            throw new HttpRequestException(error, inner: null, statusCode: resp.StatusCode);
        }
        return resp;
    }
}
