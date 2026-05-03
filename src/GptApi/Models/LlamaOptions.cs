namespace GptApi.Models;

public sealed class LlamaOptions
{
    public string WorkerUrl { get; set; } = "http://localhost:9000";

    public int RequestTimeoutSeconds { get; set; } = 300;
}
