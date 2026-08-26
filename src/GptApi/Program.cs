using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GptApi.Auth;
using GptApi.Endpoints;
using GptApi.Handlers;
using GptApi.Http;
using GptApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlamaOptions>(builder.Configuration.GetSection("Llama"));
builder.Services.Configure<ApiKeyAuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddMemoryCache();

// One HttpClient per worker backend (PC GPU, NAS A380, NAS CPU). LlamaRouter resolves a
// request's model to a backend and runs the call with transport-level failover.
var llamaOptions = builder.Configuration.GetSection("Llama").Get<LlamaOptions>() ?? new LlamaOptions();
foreach (var backend in llamaOptions.EffectiveBackends())
{
    builder.Services.AddHttpClient(LlamaRouter.ClientName(backend.Name), http =>
    {
        http.BaseAddress = new Uri(backend.Url);
        http.Timeout = TimeSpan.FromSeconds(llamaOptions.RequestTimeoutSeconds);
    });
}

builder.Services.AddSingleton<LlamaRouter>();
builder.Services.AddSingleton<ModelAliasResolver>();

builder.Services.AddScoped<ChatHandler>();
builder.Services.AddScoped<ModelsHandler>();
builder.Services.AddScoped<EmbeddingsHandler>();
builder.Services.AddScoped<RerankHandler>();

// Liveness (/livez) + readiness (/readyz, pings the llama-server worker) probes.
builder.Services.AddAppHealthChecks();

builder.Services
    .AddAuthentication(ApiKeyAuthOptions.SchemeName)
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthOptions.SchemeName, opts =>
    {
        var section = builder.Configuration.GetSection("Auth");
        section.Bind(opts);
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var defaultRpm = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 30);
    var authConfig = builder.Configuration.GetSection("Auth").Get<ApiKeyAuthOptions>() ?? new();
    // Each key's own requests/min budget (falls back to the global default), so assistant / mtg /
    // demos get independent buckets and one busy consumer can't starve the others.
    var rpmByName = authConfig.ApiKeys
        .Where(k => !string.IsNullOrEmpty(k.Name))
        .GroupBy(k => k.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last().RequestsPerMinute ?? defaultRpm, StringComparer.Ordinal);

    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var name = ctx.User.Identity?.Name;
        var key = name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        var limit = name is not null && rpmByName.TryGetValue(name, out var rpm) ? rpm : defaultRpm;
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limit,
            TokensPerPeriod = limit,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

var allowedOrigins = builder.Configuration.GetSection("Auth:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// Chat contexts can carry a few thousand tokens of system prompt + conversation history,
// but rarely exceed 1 MiB of JSON. 4 MiB leaves comfortable headroom.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4 * 1024 * 1024);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "GptApi",
            Version = "v1",
            Description =
                "Self-hosted, OpenAI-compatible LLM API powered by llama.cpp. " +
                "Drop-in replacement for `api.openai.com/v1/*` for any client that " +
                "supports a configurable base URL. Authenticate with your key in the " +
                "`X-API-Key` header.",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Bearer token. Send `Authorization: Bearer <key>`. " +
                "Standard for OpenAI-compatible clients.",
        };
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas["ProblemDetails"] = ProblemDetailsSchema();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthOptions.HeaderName,
            Description = "API key. Send in the X-API-Key header. " +
                "Used by KokoroApi/FlorenceApi-style internal callers.",
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
            AddProblem(operation, context.Document, StatusCodes.Status401Unauthorized, "Unauthorized");
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", context.Document)] = new List<string>(),
            });
        }

        // The cross-cutting code no endpoint declares — ProblemExceptionHandler produces it.

        AddProblem(operation, context.Document, StatusCodes.Status500InternalServerError, "Internal server error");

        // Bodyless 4xx/5xx come from the non-generic arms of the typed-result unions (NotFound,

        // UnauthorizedHttpResult). UseStatusCodePages fills them at runtime, so declare the shape.

        foreach (var code in operation.Responses?.Keys.ToList() ?? [])
        {
            if (code.Length != 3 || code[0] is not ('4' or '5')) continue;

            var existing = operation.Responses![code];

            if (existing.Content is { Count: > 0 }) continue;

            operation.Responses[code] = new OpenApiResponse
            { Description = existing.Description, Content = ProblemContent(context.Document) };
        }

        return Task.CompletedTask;
    });
});

// Every error response carries the same shape, so a generated client types its error once instead of
// falling back to `void`.
static Dictionary<string, OpenApiMediaType> ProblemContent(OpenApiDocument document) =>
    new() { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) } };

static void AddProblem(OpenApiOperation operation, OpenApiDocument document, int status, string description)
{
    var code = status.ToString(CultureInfo.InvariantCulture);
    operation.Responses ??= [];
    if (operation.Responses.ContainsKey(code)) return;
    operation.Responses[code] = new OpenApiResponse { Description = description, Content = ProblemContent(document) };
}

// RFC 9457. Declared here because nothing returns the CLR type directly, so the generator never emits it.
static OpenApiSchema ProblemDetailsSchema() => new()
{
    Type = JsonSchemaType.Object,
    Description = "RFC 9457 problem details.",
    Properties = new Dictionary<string, IOpenApiSchema>
    {
        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
    },
};

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "gpt-api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
        .WithTracing(t => t
            .AddSource("GptApi.Chat")
            .AddAspNetCoreInstrumentation(o =>
            {
                o.RecordException = true;
                // Health probes are polled constantly by docker + devops-monitor; their spans add nothing.
                o.Filter = ctx => ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz";
            })
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

    builder.Logging.AddOpenTelemetry(o =>
    {
        o.IncludeFormattedMessage = true;
        o.IncludeScopes = true;
        o.AddOtlpExporter();
    });
}

var app = builder.Build();

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

if (allowedOrigins.Length > 0) app.UseCors();
app.UseExceptionHandler();
// Fills the empty body of a bare 4xx (auth challenges, TypedResults.NotFound) with
// ProblemDetails, so the spec's promise holds.
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("GptApi")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapAppHealthChecks(app.Environment);
app.MapModelsEndpoint().RequireAuthorization();
app.MapChatCompletions().RequireAuthorization();
app.MapCompletions().RequireAuthorization();
app.MapEmbeddings().RequireAuthorization();
app.MapRerank().RequireAuthorization();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
