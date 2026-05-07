# Design notes

## 1. Why OIDC, not SAML

OIDC pros:
- JSON / REST native (easy to debug)
- Mature .NET libraries
- Mobile-friendly (matters for future)
- Faster setup for SME admins

SAML pros:
- More common in enterprise / regulated industries
- Built into legacy ADFS / older identity stacks

Decision: OIDC first; SAML later if a customer's IdP doesn't support OIDC. Most modern providers (Entra, Google, Okta, OneLogin, AWS SSO) all do OIDC natively.

## 2. Why per-tenant config in DB, not appsettings

For SaaS-like multi-tenant rollout (even though we're single-tenant per deploy currently), each customer gets their own client_id / client_secret. DB-stored config is editable via admin UI without redeploy.

Single-deploy customers (on-prem) still benefit: customer's IT changes IdP without touching deployment.

## 3. JIT provisioning trade-offs

**JIT enabled (default)**: faster onboarding, no manual sync needed. Risk: if IdP changes domain ownership, attackers could provision unauthorized accounts.

Mitigations:
- AllowedEmailDomains enforces "only @acme.com or @acme.com.tw"
- JIT-created users get NO roles by default unless RoleMapping fires
- Audit event on every JIT creation (Category = Auth, Action = "user.jit_provisioned")

**JIT disabled**: customer pre-provisions users via HR sync; SSO only authenticates known users. Higher security; slower onboarding.

Default JIT on; admins can disable per their threat model.

## 4. Role mapping evaluation timing

Two strategies:

A. Evaluate on every login → role assignments always reflect current IdP groups. Cost: extra DB writes per login.
B. Evaluate only on first JIT provisioning → set-and-forget; manual changes possible later.

Decision: A. Login is infrequent; the cost is invisible. Sync = source-of-truth wins (IdP is authoritative).

## 5. Token verification

OIDC provider returns ID token (JWT). We MUST verify:
- Signature (provider's JWKS endpoint; cached 24h)
- `iss` matches configured IssuerUrl
- `aud` matches our ClientId
- `exp` not expired
- `nonce` matches the one we put in the authorize request (replay protection)

`Microsoft.IdentityModel.Tokens` library handles signature + standard claims. Our code adds the nonce + email-domain checks.

## 6. State / nonce / PKCE

To protect against CSRF + replay:
- `state`: random GUID stored in HTTP-only cookie; verified on callback
- `nonce`: random GUID embedded in authorize request; verified inside ID token
- PKCE: code_challenge sent in authorize, code_verifier in token request — defense-in-depth, especially for SPA-style apps

All three implemented.

## 7. Logout

Two scopes:
- BPM logout: clear local JWT, redirect to BPM home
- IdP logout (single sign-out): call provider's end_session endpoint with id_token_hint

If `LogoutAsync` returns the IdP end-session URL, the frontend redirects there; the IdP then redirects back to BPM. This signs out all SSO-connected apps simultaneously.

For IdPs that don't support end-session (some legacy OIDC), BPM-only logout is the fallback.

## 8. Open questions

- **Group claim format variance**: Entra returns object IDs, not names. Mapping must support both. Document in setup help.
- **Token refresh**: do we use refresh tokens to keep users logged in beyond ID-token expiration? Yes, but BPM JWT is independent — when BPM JWT expires (8h dev / 1h prod), user re-authenticates against IdP silently if session active.
- **Multiple email aliases**: an employee with `wilson@acme.com` and `wilson.lin@acme.com`. Provider chooses one as primary; we match against that. If they switch primary later, our User row still uses the original email — admin manually merges.
- **Custom claim sources**: some IdPs need extra config to emit `groups` claim. Document each provider's setup.
