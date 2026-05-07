## Why

`bpm-auth-jwt` (already shipped) authenticates via dev-login + persona for development. Production needs:

- Customers want their employees to log in via existing corporate identity (Entra ID / Azure AD / Google Workspace / Okta)
- IT does NOT want to manage another set of passwords
- Compliance / audit demands SSO with MFA / device policies enforced upstream
- Onboarding a new employee should "just work" once HR adds them in their identity provider

This change ships OpenID Connect (OIDC) integration for two priority providers (Entra ID + Google Workspace) with provider-agnostic plumbing for adding more later (Okta / OneLogin / generic OIDC).

## What Changes

### SSO capability (NEW `bpm-sso-oidc`)

**Configuration** — per-tenant `SsoConfiguration`:

- `Id`, `TenantId`, `Provider` (enum: `EntraId` / `GoogleWorkspace` / `GenericOidc`)
- `IssuerUrl` — provider's OIDC issuer URL
- `ClientId`, `ClientSecret` (encrypted at rest via DPAPI / Azure Key Vault / env)
- `AllowedEmailDomains[]` — only emails matching are allowed; protect against tenant cross-pollination
- `JustInTimeProvisioning` (bool) — auto-create User on first login if email matches allowed domain; defaults true
- `RoleMappingJson` — optional: provider claims → app roles (e.g., "if `groups` includes 'BPM-Admins' → tenant_admin")
- `IsActive`

**Service** `ISsoLoginService`:

- `GetAuthorizationUrlAsync(tenantId, redirectUri)` — returns the OIDC authorize URL for the configured provider
- `HandleCallbackAsync(tenantId, code, state)` — exchange code for ID token; verify signature; extract claims; provision (JIT) or look up User by email; mint our own JWT; return `{ token, user }`
- `LogoutAsync(tenantId, returnUrl)` — initiates OIDC end-session if provider supports

### Auth flow (high-level)

```
Browser → /login (BPM)
  → BPM redirects to provider's authorize URL (with state, nonce)
Browser → provider login + consent
  → provider redirects back to /sso/callback?code=...&state=...
BPM → exchanges code for ID token at provider's token endpoint
  → verifies ID token signature, validates aud/iss/exp/nonce
  → extracts email + name + (optional groups)
  → looks up User by email; if not found and JIT enabled, creates User + assigns default roles
  → mints BPM JWT (same shape as dev-login)
  → returns BPM JWT to browser; sets in localStorage
Browser → uses BPM JWT for subsequent API calls
```

### Configuration UI in System Admin

`/admin/sso-config`:

- Provider picker (Entra / Google / Generic)
- Form for issuer URL, client id, client secret (write-only display; show last 4 chars)
- Allowed email domains list
- JIT toggle
- Role mapping JSON editor (with validate button)
- Test login button

### Just-in-Time provisioning

When a user successfully authenticates with the IdP and their email isn't in the User table:

- If `AllowedEmailDomains` matches → INSERT User with email, full_name (from claims), is_active = true, no manager (admin enriches later via HR sync), no roles unless RoleMapping fires
- If domain doesn't match → reject login with "your email is not authorized for this tenant"
- If JIT disabled → reject login even with matching domain ("user not provisioned")

### Role mapping

Optional declarative mapping from provider claims to roles:

```json
{
  "rules": [
    { "match": { "claim": "groups", "contains": "BPM-Admins" }, "assign_role": "tenant_admin" },
    { "match": { "claim": "groups", "contains": "BPM-FlowAdmin-LEAVE" }, "assign_role": "flow_admin", "scope_ref": "LEAVE" },
    { "match": { "claim": "department", "equals": "Finance" }, "assign_role": "flow_admin", "scope_ref": "PURCHASE" }
  ]
}
```

Evaluated on every login (not just JIT) to keep roles in sync with IdP. Removed group memberships → role assignment removed (IdP is source of truth).

### Provider-specific quirks

- **Entra ID**: discovery URL pattern `https://login.microsoftonline.com/{tenant}/v2.0`. Group claims need explicit token configuration on app registration; document the setup steps in admin UI help.
- **Google Workspace**: discovery `https://accounts.google.com`. Domain claim available via `hd` field; useful for AllowedEmailDomains validation.
- **Generic**: discovery URL configured manually; use `openid email profile` scope; group claim configurable.

### Coexistence with dev-login

`bpm-auth-jwt`'s dev-login remains for development (`BPM_AUTH_MODE=dev`). In production (`BPM_AUTH_MODE=prod`):

- Dev-login endpoint returns 404
- SSO is the only path
- For local dev with SSO testing, set `BPM_AUTH_MODE=dev_with_sso` (new mode) — both endpoints work

### Out of scope (future changes)

- SAML 2.0 (only OIDC in v1; SAML deferred until customer demand)
- Multi-IdP per tenant (one SSO config per tenant for now)
- Session step-up (require MFA for sensitive actions)
- Token revocation propagation to BPM-issued JWTs (BPM JWT lifetime is short; live with the gap)
- IdP-initiated SSO (only SP-initiated supported)
- Enterprise federation across tenants (each tenant = its own SSO config)

## Capabilities

### New Capabilities

- `bpm-sso-oidc` — SsoConfiguration entity, ISsoLoginService, OIDC authorize / callback / logout endpoints, JIT provisioning, role mapping evaluator, provider-specific discovery (Entra / Google / Generic), per-tenant config UI in System Admin.

### Modified Capabilities

- `bpm-auth-jwt` — formalize the BPM JWT shape so SSO and dev-login produce the same token; add `BPM_AUTH_MODE=dev_with_sso` for hybrid local dev.

## Impact

- **bpm-svc/src/Domain/Entities/Auth/SsoConfiguration.cs**: new entity
- **bpm-svc/src/Application/Auth/ISsoLoginService.cs / SsoLoginService.cs**: orchestration
- **bpm-svc/src/Application/Auth/RoleMappingEvaluator.cs**: claim-to-role rules
- **bpm-svc/src/Application/Auth/IIdentityProviderClient.cs**: thin OIDC client
- **bpm-svc/src/Infrastructure/Auth/EntraIdProviderClient.cs / GoogleProviderClient.cs / GenericOidcProviderClient.cs**: provider quirks
- **bpm-svc/src/Persistence/Configurations/Auth/SsoConfigurationConfiguration.cs**: EF
- **bpm-svc/src/Persistence/Migrations/AddSsoConfiguration**: 1 new table
- **bpm-svc/src/Api/Auth/SsoController.cs**: 3 endpoints (authorize, callback, logout)
- **bpm-svc/src/Api/Auth/SsoConfigController.cs**: 5 admin endpoints (list, get, create, update, test)
- **bpm-ui/src/screens/Login.tsx**: NEW — provider button + dev-login fallback
- **bpm-ui/src/screens/admin/sso-config/SsoConfigForm.tsx**: NEW
- **bpm-ui/src/lib/auth.ts**: SSO redirect helpers
- **NuGet**: `Microsoft.AspNetCore.Authentication.OpenIdConnect` (already in framework); `IdentityModel` for OIDC discovery
- **DB migration**: 1 new table
- **Demo guard**: 9 mock-up forms NOT modified
