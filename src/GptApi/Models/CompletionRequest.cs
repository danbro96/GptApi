using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class CompletionRequest
{
    public required string Model { get; set; }

    /// <summary>
    /// String or array of strings. Kept raw to preserve either form.
    /// </summary>
    public required JsonElement Prompt { get; set; }

    public bool Stream { get; set; }

    public int? MaxTokens { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public int? Seed { get; set; }

    public JsonElement? Stop { get; set; }

    public string? User { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
