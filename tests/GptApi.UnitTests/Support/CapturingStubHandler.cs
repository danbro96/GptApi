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
