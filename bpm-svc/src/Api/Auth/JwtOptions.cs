namespace Bpm.Api.Auth;

/// Configuration for JWT issuance and validation.
/// Secret comes from `BPM_JWT_SECRET` env var (≥32 bytes); other fields
/// fall back to defaults appropriate for dev.
public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "bpm-svc";
    public string Audience { get; set; } = "bpm-ui";
    public TimeSpan ExpiryDev { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan ExpiryProd { get; set; } = TimeSpan.FromHours(1);
}
