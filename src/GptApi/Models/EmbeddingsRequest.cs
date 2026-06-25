using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class EmbeddingsRequest
{
    public required string Model { get; set; }

    /// <summary>
    /// String, array of strings, or token arrays. Kept raw to preserve whichever form the
    /// client sent (and forwarded to the worker untouched).
    /// </summary>
    public required JsonElement Input { get; set; }

    public string? User { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
