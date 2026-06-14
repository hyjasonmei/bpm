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
    // Token TTL = 1 day (both dev + prod). One login lasts a working day before
    // re-auth — long enough for an uninterrupted demo / admin session, short
    // enough to bound a leaked bearer.
    public TimeSpan ExpiryDev { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan ExpiryProd { get; set; } = TimeSpan.FromDays(1);
}
