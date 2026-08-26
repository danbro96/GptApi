using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GptApi.UnitTests.Support;

internal static class TestJson
{
    /// <summary>The JSON options Program configures, so test serialization matches the wire contract.</summary>
    public static IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> Options()
    {
        var o = new Microsoft.AspNetCore.Http.Json.JsonOptions();
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.SerializerOptions.PropertyNameCaseInsensitive = true;
        o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return Microsoft.Extensions.Options.Options.Create(o);
    }
}
