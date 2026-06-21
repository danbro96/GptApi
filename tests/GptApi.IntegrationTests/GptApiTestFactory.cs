using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GptApi.IntegrationTests;

/// <summary>Hosts the real app in-process. The llama-server upstream is reached lazily over HttpClient and is never
/// called by these smoke tests (they assert boot, OpenAPI, and the auth gate only), so no stub is required.</summary>
public sealed class GptApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}
