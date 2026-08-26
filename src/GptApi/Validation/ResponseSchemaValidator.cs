using System.Text.Json;
using GptApi.Dtos;
using Json.Schema;

namespace GptApi.Validation;

/// <summary>Validates a worker chat response against the caller's <c>response_format.json_schema</c> — the
/// defense-in-depth layer behind the worker's grammar-constrained decoding. Each choice's message content is
/// expected to be a JSON string conforming to the schema.</summary>
public static class ResponseSchemaValidator
{
    public static SchemaVerdict Validate(ChatCompletionResponse response, JsonElement schemaElement)
    {
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaElement.GetRawText());
        }
        catch (JsonException)
        {
            // A caller schema we can't parse isn't the worker's fault — don't fail the response over it.
            return SchemaVerdict.Pass;
        }

        var options = new EvaluationOptions { OutputFormat = OutputFormat.List };

        foreach (var choice in response.Choices)
        {
            var content = choice.Message.Content;
            if (content.ValueKind != JsonValueKind.String)
                return SchemaVerdict.Fail("response content was not a JSON string");

            JsonDocument instance;
            try
            {
                instance = JsonDocument.Parse(content.GetString()!);
            }
            catch (JsonException)
            {
                return SchemaVerdict.Fail("response content was not valid JSON");
            }

            using (instance)
            {
                var results = schema.Evaluate(instance.RootElement, options);
                if (!results.IsValid)
                    return SchemaVerdict.Fail(Describe(results));
            }
        }

        return SchemaVerdict.Pass;
    }

    private static string Describe(EvaluationResults results)
    {
        var errors = results.Details
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Take(5);
        var joined = string.Join("; ", errors);
        return string.IsNullOrEmpty(joined) ? "did not match the required schema" : joined;
    }
}
