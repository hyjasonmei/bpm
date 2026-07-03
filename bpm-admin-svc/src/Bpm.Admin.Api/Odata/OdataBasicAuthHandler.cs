using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Bpm.Admin.Api.Odata;

/// <summary>
/// HTTP Basic authentication for the /odata integration surface, using a single
/// dedicated integration credential from config (OData:User / OData:Password,
/// i.e. env OData__User / OData__Password). Separate from the app's JWT bearer
/// so an external system (BI / HR / iPaaS) authenticates with its own account.
/// Returns 401 with a WWW-Authenticate challenge when missing/invalid. If no
/// credential is configured, the scheme fails everything (endpoint stays closed).
/// </summary>
public sealed class OdataBasicAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration config) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OdataBasic";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cfgUser = config["OData:User"];
        var cfgPass = config["OData:Password"];
        if (string.IsNullOrEmpty(cfgUser) || string.IsNullOrEmpty(cfgPass))
            return Task.FromResult(AuthenticateResult.Fail("OData integration credential not configured"));

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!AuthenticationHeaderValue.TryParse(authHeader.ToString(), out var parsed)
            || !"Basic".Equals(parsed.Scheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(parsed.Parameter))
            return Task.FromResult(AuthenticateResult.NoResult());

        string user, pass;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
            var i = raw.IndexOf(':');
            if (i < 0) return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials"));
            user = raw[..i]; pass = raw[(i + 1)..];
        }
        catch { return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials")); }

        var ok = CryptographicEquals(user, cfgUser) & CryptographicEquals(pass, cfgPass);
        if (!ok) return Task.FromResult(AuthenticateResult.Fail("Invalid integration credentials"));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user),
            new Claim("integration", "odata"),
        }, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"odata\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }

    // Length-independent constant-time-ish compare to avoid trivial timing leaks.
    private static bool CryptographicEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Security.Cryptography.SHA256.HashData(ba),
            System.Security.Cryptography.SHA256.HashData(bb));
    }
}
