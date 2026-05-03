namespace GptApi.Models;

public sealed class ChatCompletionUsage
{
    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }
}
