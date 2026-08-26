using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GptApi.UnitTests.Support;

/// <summary>Hands every named backend the same stub handler.</summary>
internal sealed class SingleBackendFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub.local") };
}
