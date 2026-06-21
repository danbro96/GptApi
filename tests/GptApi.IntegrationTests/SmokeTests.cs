using System.Net;
using Xunit;

namespace GptApi.IntegrationTests;

/// <summary>Boot smoke: the app starts in-process, serves health + OpenAPI anonymously, and gates the
/// OpenAI-compatible surface behind the API-key / Bearer scheme.</summary>
public sealed class SmokeTests(GptApiTestFactory factory) : IClassFixture<GptApiTestFactory>
{
    [Fact]
    public async Task Livez_is_ok()
    {
        var resp = await factory.CreateClient().GetAsync("/livez");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var resp = await factory.CreateClient().GetAsync("/openapi/v1.json");
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Models_without_a_key_is_unauthorized()
    {
        var resp = await factory.CreateClient().GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
