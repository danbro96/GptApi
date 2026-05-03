namespace GptApi.Models;

public sealed class LlamaOptions
{
    public string WorkerUrl { get; set; } = "http://localhost:9000";

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Model id reported via <c>GET /v1/models</c> and accepted in request bodies.
    /// llama-server only loads one model at a time; this is just the label clients use.
    /// </summary>
    public string ServedModelId { get; set; } = "local-llm";
}
