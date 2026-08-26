namespace GptApi.Dtos;

public sealed class EmbeddingsUsage
{
    public int PromptTokens { get; set; }

    public int TotalTokens { get; set; }
}
