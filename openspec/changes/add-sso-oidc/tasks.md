# Tasks

## 1. Domain + persistence

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Auth/SsoProvider.cs` enum (EntraId, GoogleWorkspace, GenericOidc)
- [ ] 1.2 Create `SsoConfiguration.cs` entity with encrypted ClientSecret
- [ ] 1.3 EF config; migration `AddSsoConfiguration`
- [ ] 1.4 Encryption: `IDataProtector` for client secret column

## 2. OIDC client + provider implementations

- [ ] 2.1 Add NuGet `IdentityModel` (industry-standard OIDC client)
- [ ] 2.2 Create `IIdentityProviderClient.cs` with Discover / ExchangeCode / VerifyIdToken
- [ ] 2.3 Implement EntraIdProviderClient, GoogleProviderClient, GenericOidcProviderClient
- [ ] 2.4 Per-provider quirks: Entra group claim configuration; Google `hd` validation
- [ ] 2.5 JWKS cache (24h)

## 3. SSO login service

- [ ] 3.1 Create `ISsoLoginService.cs`
- [ ] 3.2 Implement `SsoLoginService.cs`:
  - GetAuthorizationUrlAsync: build URL with state / nonce / PKCE
  - HandleCallbackAsync: verify state cookie, exchange code, verify ID token, resolve User (lookup by email; JIT or reject), evaluate RoleMapping, mint BPM JWT
  - LogoutAsync: build IdP end-session URL or local-only logout
- [ ] 3.3 Wire DI

## 4. Role mapping evaluator

- [ ] 4.1 Create `RoleMappingEvaluator.cs`
- [ ] 4.2 Parse RoleMappingJson rules; evaluate against ID token claims
- [ ] 4.3 Apply: ensure RoleAssignments match the evaluation; revoke ones no longer matching
- [ ] 4.4 Unit tests with various rule shapes

## 5. API endpoints

- [ ] 5.1 Create `bpm-svc/src/Api/Auth/SsoController.cs`:
  - `GET /api/sso/authorize` — initiates flow, sets state cookie, redirects
  - `GET /api/sso/callback` — handles callback; mints JWT; returns to frontend with token
  - `POST /api/sso/logout` — clears local + returns IdP end-session URL
- [ ] 5.2 Create `SsoConfigController.cs` (admin):
  - GET /api/admin/sso-config — current tenant config
  - PUT /api/admin/sso-config — update
  - POST /api/admin/sso-config/test — test login flow without committing
  - POST /api/admin/sso-config/clear — disable SSO for tenant

## 6. Frontend

- [ ] 6.1 Create `bpm-ui/src/screens/Login.tsx`:
  - When tenant has SSO config: shows provider button (e.g., "Sign in with Microsoft")
  - Click → redirects to /api/sso/authorize
  - Fallback to dev-login if `BPM_AUTH_MODE=dev` or `dev_with_sso`
- [ ] 6.2 Update `bpm-ui/src/lib/auth.ts` to handle SSO callback (extract token from URL fragment, store, redirect)
- [ ] 6.3 Create `bpm-ui/src/screens/admin/sso-config/SsoConfigForm.tsx`:
  - Provider picker
  - Issuer URL / ClientId / ClientSecret inputs (secret write-only display)
  - Allowed domains array editor
  - JIT toggle
  - Role mapping JSON textarea with validate button
  - Test login button (opens callback in popup; reports success/failure)

## 7. Auth mode integration

- [ ] 7.1 Extend `BPM_AUTH_MODE` enum: `dev` (only dev-login), `prod` (only SSO), `dev_with_sso` (both)
- [ ] 7.2 Update Program.cs to enable endpoints based on mode
- [ ] 7.3 Document in SETUP.md

## 8. End-to-end verification

- [ ] 8.1 `dotnet build` clean
- [ ] 8.2 Apply migration; verify SsoConfigurations table
- [ ] 8.3 Configure SSO for a test tenant (use a free Auth0 tenant or Google playground for testing)
- [ ] 8.4 From bpm-ui, click "Sign in with..."; complete OIDC flow with a test account; verify JIT provisions user; verify BPM JWT issued; can call /api/me
- [ ] 8.5 Test failed login (wrong domain): verify rejection with clear error
- [ ] 8.6 Test role mapping: account in test group → tenant_admin role assigned
- [ ] 8.7 Logout: verify both BPM-local + IdP end-session
- [ ] 8.8 **Demo guard**: 9 mock-up forms NOT modified

## 9. Commit

- [ ] 9.1 Commit in chunks (entity + migration; provider clients; service; endpoints; frontend; verification)
- [ ] 9.2 Push via GitKraken
