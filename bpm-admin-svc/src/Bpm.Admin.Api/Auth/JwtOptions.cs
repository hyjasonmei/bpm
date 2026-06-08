namespace Bpm.Admin.Api.Auth;

/// <summary>
/// JWT signing/validation parameters for admin-svc. Values are deliberately
/// identical to bpm-svc's <c>Bpm.Api.Auth.JwtOptions</c> (issuer "bpm-svc",
/// audience "bpm-ui") so a token minted by admin-svc's /api/auth/login is
/// accepted by bpm-svc too — the unify-jwt design: both services share the
/// Admin_* identity store, the BPM_JWT_SECRET, and these issuer/audience
/// values, so one login yields a token good against both APIs.
/// </summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "bpm-svc";
    public string Audience { get; set; } = "bpm-ui";
    public TimeSpan ExpiryDev { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan ExpiryProd { get; set; } = TimeSpan.FromHours(1);
}
