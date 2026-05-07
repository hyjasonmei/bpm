## ADDED Requirements

### Requirement: SsoConfiguration per tenant configures the OIDC provider

The system SHALL persist `SsoConfiguration` per tenant carrying provider type, IssuerUrl, ClientId, encrypted ClientSecret, AllowedEmailDomains[], JustInTimeProvisioning flag, optional RoleMappingJson. ClientSecret SHALL be encrypted at rest using `IDataProtector`. Admins MAY view but the response SHALL show only the last 4 characters of the secret.

#### Scenario: Configure Entra ID

- **WHEN** an admin POSTs `/api/admin/sso-config` with provider=EntraId, issuer URL, clientId, clientSecret, allowed domain
- **THEN** the row is persisted; secret is encrypted; subsequent GET returns `clientSecret = "...abcd"` (last 4)

### Requirement: OIDC authorize / callback / logout endpoints

The system SHALL expose:

- `GET /api/sso/authorize?tenant_id=` — sets state + nonce + PKCE cookies; redirects to provider's authorize endpoint
- `GET /api/sso/callback?code=&state=` — verifies state cookie; exchanges code for ID token at provider; verifies ID token signature, iss, aud, exp, nonce; resolves User by email; mints BPM JWT; redirects to frontend with token
- `POST /api/sso/logout` — clears local session; returns IdP end-session URL when supported

#### Scenario: Successful login flow

- **GIVEN** SSO is configured for tenant Acme with Entra ID
- **WHEN** Wilson clicks "Sign in with Microsoft" → redirects to /api/sso/authorize → onward to Entra
- **AND** Wilson authenticates at Entra → callback comes back with code
- **THEN** /api/sso/callback verifies token, looks up User wilson@acme.com (or JIT-provisions), mints BPM JWT, redirects browser to /

#### Scenario: Forged state rejected

- **WHEN** /api/sso/callback receives a code with mismatched state cookie
- **THEN** the response is 400 with "state mismatch — possible CSRF"

#### Scenario: Expired ID token rejected

- **WHEN** the callback receives an ID token whose exp claim is in the past
- **THEN** the response is 401 with "ID token expired"

### Requirement: Just-in-Time provisioning gated by allowed domains

The system SHALL provision Users on first SSO login only when JIT is enabled AND the email domain matches `AllowedEmailDomains`. When an authenticated user's email is NOT in the User table:

- If `JustInTimeProvisioning = true` AND email domain matches → INSERT User with email, full_name from claims, IsActive = true
- If domain doesn't match → reject login with "your email is not authorized for this tenant"
- If JIT disabled → reject login with "user not provisioned; contact admin"

JIT-created users SHALL receive no roles by default unless RoleMapping rules match.

#### Scenario: New user JIT provisioned

- **GIVEN** SSO config has JIT=true, allowed_domains=["acme.com"]
- **WHEN** wilson@acme.com authenticates for the first time
- **THEN** a User row is INSERTED; JWT minted; subsequent calls work

#### Scenario: Wrong domain rejected

- **GIVEN** the same config; wilson@evil.com tries to authenticate
- **THEN** the callback returns 403 with "your email domain is not authorized"
- **AND** an audit row records the rejected attempt

#### Scenario: JIT disabled

- **GIVEN** JIT=false; wilson@acme.com is not in User table
- **WHEN** wilson authenticates
- **THEN** the callback returns 403 with "user not provisioned"

### Requirement: Role mapping evaluated on every login

The `RoleMappingEvaluator` SHALL evaluate RoleMappingJson rules against the ID token claims on every successful login, not only on JIT provisioning. Role assignments SHALL be aligned with current rule outcomes — assignments matching no longer-matching rules SHALL be revoked.

#### Scenario: Group membership grants role

- **GIVEN** RoleMappingJson has rule `{ match: { claim: "groups", contains: "BPM-Admins" }, assign_role: "tenant_admin" }`
- **WHEN** Wilson logs in with `groups` claim containing "BPM-Admins"
- **THEN** Wilson is assigned tenant_admin role for this tenant (idempotent — no duplicate row)

#### Scenario: Removed group revokes role

- **GIVEN** Wilson previously had tenant_admin via the same rule
- **AND** today his ID token's groups claim no longer contains "BPM-Admins"
- **WHEN** Wilson logs in
- **THEN** his tenant_admin assignment is removed (synced with current claims)

### Requirement: BPM_AUTH_MODE controls available authentication methods

The system SHALL expose authentication endpoints based on `BPM_AUTH_MODE` env value:

- `dev` — only dev-login endpoint enabled; SSO endpoints return 404
- `prod` — only SSO endpoints enabled; dev-login returns 404
- `dev_with_sso` — both enabled (for local development with SSO testing)

Startup MUST fail fast if `BPM_AUTH_MODE=prod` and no SsoConfiguration exists.

#### Scenario: Prod without SSO config fails

- **GIVEN** BPM_AUTH_MODE=prod and no SsoConfigurations row
- **WHEN** the app starts
- **THEN** startup throws with "BPM_AUTH_MODE=prod requires at least one SsoConfiguration"

#### Scenario: Dev mode hides SSO

- **GIVEN** BPM_AUTH_MODE=dev
- **WHEN** a client calls /api/sso/authorize
- **THEN** 404
