using System.Text.Json;
using System.Text.Json.Serialization;
using GptApi.Models;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>The wire contract that makes this an OpenAI-compatible endpoint: snake_case in/out, null omission,
/// and the fixed <c>object</c> discriminators. Mirrors the JSON options configured in Program.cs.</summary>
public class OpenAiContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void ChatCompletionRequest_deserializes_openai_snake_case()
    {
        const string json = """
            {"model":"qwen3-8b","messages":[{"role":"user","content":"hi"}],"max_tokens":64,"temperature":0.7,"stream":true}
            """;

        var req = JsonSerializer.Deserialize<ChatCompletionRequest>(json, Json)!;

        Assert.Equal("qwen3-8b", req.Model);
        Assert.Equal(64, req.MaxTokens);
        Assert.Equal(0.7f, req.Temperature);
        Assert.True(req.Stream);
        Assert.Single(req.Messages);
        Assert.Equal("user", req.Messages[0].Role);
    }

    [Fact]
    public void ModelsResponse_serializes_to_openai_list_shape()
    {
        var resp = new ModelsResponse { Data = [new ModelInfo { Id = "qwen3-8b" }] };

        var json = JsonSerializer.Serialize(resp, Json);

        Assert.Contains("\"object\":\"list\"", json);
        Assert.Contains("\"object\":\"model\"", json);
        Assert.Contains("\"id\":\"qwen3-8b\"", json);
        Assert.Contains("\"owned_by\":\"local\"", json);
    }

    [Fact]
    public void Null_optionals_are_omitted_on_the_wire()
    {
        var req = new ChatCompletionRequest { Model = "m", Messages = [] };
        var json = JsonSerializer.Serialize(req, Json);
        Assert.DoesNotContain("max_tokens", json);   // null -> omitted
        Assert.DoesNotContain("temperature", json);
    }
}
