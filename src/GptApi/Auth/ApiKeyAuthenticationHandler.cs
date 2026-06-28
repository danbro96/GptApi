using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace GptApi.Auth;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private const string BearerPrefix = "Bearer ";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractKey(Request);
        if (string.IsNullOrEmpty(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        var match = MatchKey(presented, Options.ApiKeys);
        if (match is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, match.Name),
            new Claim("api_key_name", match.Name),
            new Claim("api_key_priority", match.Priority.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string? ExtractKey(HttpRequest req)
    {
        // Authorization: Bearer <key> — what every mainstream OpenAI-compat client sends.
        if (req.Headers.TryGetValue("Authorization", out var auth) && auth.Count > 0)
        {
            var value = auth[0];
            if (!string.IsNullOrEmpty(value)
                && value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = value.AsSpan(BearerPrefix.Length).Trim();
                if (token.Length > 0) return token.ToString();
            }
        }

        if (req.Headers.TryGetValue(ApiKeyAuthOptions.HeaderName, out var hdr) && hdr.Count > 0)
            return hdr[0];
        if (req.Query.TryGetValue(ApiKeyAuthOptions.QueryName, out var qs) && qs.Count > 0)
            return qs[0];
        return null;
    }

    private static ApiKeyEntry? MatchKey(string presented, IReadOnlyList<ApiKeyEntry> keys)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        foreach (var entry in keys)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            var keyBytes = Encoding.UTF8.GetBytes(entry.Key);
            if (CryptographicOperations.FixedTimeEquals(presentedBytes, keyBytes))
                return entry;
        }

        return null;
    }
}
