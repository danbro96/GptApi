using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class RerankRequest
{
    public required string Model { get; set; }

    public required string Query { get; set; }

    public required IReadOnlyList<string> Documents { get; set; }

    public int? TopN { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
