using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GptApi.UnitTests.Support;

/// <summary>
/// In-process llama backend: captures the forwarded request body and returns a canned response,
/// so handler tests can assert what the gateway sent upstream (e.g. the alias-resolved model id).
/// </summary>
internal sealed class CapturingStubHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public string? LastBody { get; private set; }
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (request.Content is not null)
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status) { Content = new StringContent(responseJson) };
    }
}

/// <summary>Hands every named backend the same stub handler.</summary>
internal sealed class SingleBackendFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub.local") };
}

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
