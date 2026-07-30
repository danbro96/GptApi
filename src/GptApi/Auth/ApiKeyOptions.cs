using Microsoft.AspNetCore.Authentication;

namespace GptApi.Auth;

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Per-key request budget (requests/min). Null falls back to the global
    /// <c>RateLimit:RequestsPerMinute</c>, so one busy consumer can't starve the others.</summary>
    public int? RequestsPerMinute { get; set; }

    /// <summary>Optional per-key daily token budget. Config surface only for now; enforcement is a
    /// follow-up (needs a stateful per-key/day token counter).</summary>
    public long? DailyTokenBudget { get; set; }

    /// <summary>Contention hint, recorded on the principal (claim) and telemetry. Cross-key
    /// scheduling (latency-sensitive tiers winning contention) is a llama-swap-level follow-up —
    /// the ASP.NET rate limiter only arbitrates within a single key's partition.</summary>
    public KeyPriority Priority { get; set; } = KeyPriority.Normal;
}

public enum KeyPriority
{
    Low,
    Normal,
    High,
}

public sealed class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
    public const string QueryName = "api_key";

    public List<ApiKeyEntry> ApiKeys { get; set; } = new();

    public List<string> AllowedOrigins { get; set; } = new();
}
