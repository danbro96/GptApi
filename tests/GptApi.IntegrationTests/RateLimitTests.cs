using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GptApi.IntegrationTests;

/// <summary>
/// The rate limiter is partitioned per API key and sized from each key's own RequestsPerMinute, so
/// a busy consumer hits its own ceiling without affecting another key's budget.
/// </summary>
public sealed class RateLimitTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new GptApiTestFactory().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0:Key"] = "keyA",
                ["Auth:ApiKeys:0:Name"] = "alpha",
                ["Auth:ApiKeys:0:RequestsPerMinute"] = "1",
                ["Auth:ApiKeys:1:Key"] = "keyB",
                ["Auth:ApiKeys:1:Name"] = "beta",
                ["Auth:ApiKeys:1:RequestsPerMinute"] = "1",
            })));

    [Fact]
    public async Task Rate_limit_is_enforced_independently_per_key()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        // keyA's budget is 1/min: the first request passes the limiter (the backend is down, so it
        // reaches the handler and 503s), the second is rejected by the limiter before the handler.
        var a1 = await Send(client, "keyA");
        var a2 = await Send(client, "keyA");
        // keyB has its own bucket — unaffected by keyA exhausting its own.
        var b1 = await Send(client, "keyB");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, a1.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, a2.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, b1.StatusCode);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        req.Headers.Add("X-API-Key", key);
        return client.SendAsync(req);
    }
}
