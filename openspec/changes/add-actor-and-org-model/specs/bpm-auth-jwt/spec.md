## ADDED Requirements

### Requirement: JWT bearer authentication on all API endpoints

The system SHALL require a valid JWT bearer token on all `/api/*` endpoints except `/health`, `/swagger`, `OPTIONS` preflight, and the dev-login endpoint itself. The bearer SHALL be sent as `Authorization: Bearer <jwt>`. JWTs SHALL be signed HS256 with the `BPM_JWT_SECRET` env var (≥ 32 bytes).

#### Scenario: Valid JWT accepted
- **WHEN** a request hits `/api/spec` with a valid JWT in the Authorization header
- **THEN** the request is processed; the authenticated user id is available to controllers via `HttpContext.User`

#### Scenario: Missing JWT rejected
- **WHEN** a request hits `/api/spec` with no Authorization header
- **THEN** the response is HTTP 401 with a JSON body `{ "error": "missing_or_invalid_token" }`

#### Scenario: Tampered JWT rejected
- **WHEN** a JWT's payload is modified after signing
- **THEN** the response is HTTP 401

#### Scenario: Expired JWT rejected
- **WHEN** a JWT's `exp` claim is in the past
- **THEN** the response is HTTP 401 with `{ "error": "token_expired" }`

#### Scenario: Health endpoint bypasses auth
- **WHEN** a request hits `/health` with no Authorization header
- **THEN** the response is HTTP 200

#### Scenario: Startup fails on weak secret
- **WHEN** the application starts with `BPM_JWT_SECRET` shorter than 32 bytes (or unset)
- **THEN** startup aborts with a clear "BPM_JWT_SECRET must be set and ≥ 32 bytes" message

### Requirement: JWT claim shape

Issued JWTs SHALL carry the following claims:
- `sub` — `User.Id` of the authenticated user
- `persona_code` — debug aid only; authorization checks ignore this
- `tenant_id` — placeholder string (POC single-tenant)
- `roles` — JSON array of system-scope role codes assigned to this user (computed at mint time from `RoleAssignment` joined to `Role` where `scope = "system"`)
- `exp` — Unix timestamp; default 8h in dev mode, 1h in prod mode

#### Scenario: roles claim populated from RoleAssignments
- **WHEN** user X has `RoleAssignment(role: admin, scope: tenant)` and `RoleAssignment(role: viewer, scope: tenant)` (both system-scope roles)
- **AND** a JWT is minted for X
- **THEN** the JWT's `roles` claim contains `["admin", "viewer"]`

### Requirement: Dev-login endpoint mints JWT for a persona

When `BPM_AUTH_MODE=dev`, the system SHALL expose `POST /api/dev/login` accepting `{ "persona_code": "<one of: employee | manager | finance | it | hr | admin>" }`. The endpoint SHALL look up the seed user mapped to that persona (via `appsettings.Development.json` config), mint a JWT with the user's id and roles, and respond `200 { "token": "...", "user": { "id": ..., "full_name": ..., "email": ..., "department_code": ..., "persona_code": ... } }`.

#### Scenario: Valid persona returns JWT
- **WHEN** `POST /api/dev/login { "persona_code": "manager" }` is called in dev mode
- **THEN** the response is 200 with a `token` field containing a valid HS256-signed JWT for the seed manager user

#### Scenario: Unknown persona rejected
- **WHEN** `POST /api/dev/login { "persona_code": "unknown" }` is called
- **THEN** the response is 400 with `{ "error": "unknown_persona", "allowed": ["employee","manager","finance","it","hr","admin"] }`

#### Scenario: Dev-login disabled in prod mode
- **WHEN** the app starts with `BPM_AUTH_MODE=prod`
- **AND** `POST /api/dev/login` is called
- **THEN** the response is 404 (the endpoint is not registered)

### Requirement: Persona-to-user mapping via configuration

The system SHALL read the persona-to-user mapping from `appsettings.Development.json` under a `Personas` section, e.g. `{ "Personas": { "employee": "<seed_user_id_or_email>", "manager": "<...>", ... } }`. Mapping values MAY be email or Guid; the dev-login service SHALL resolve either form.

#### Scenario: Email-based mapping resolves
- **WHEN** `Personas.employee = "wilson.you@example.com"`
- **AND** a User row exists with that email
- **THEN** dev-login for `employee` mints a JWT with `sub` = that user's id

#### Scenario: Mapping missing for persona
- **WHEN** `Personas` config is missing the key for a persona that the request references
- **THEN** the dev-login response is 500 with `{ "error": "persona_mapping_missing", "persona_code": "<the missing one>" }`

### Requirement: Seed-data fixture creates persona users

The system SHALL provide a seed fixture (`scripts/seed-org-fixture.sql` or `dotnet run -- seed-org`) that, when run on an empty database, creates: ~10 users (one per persona plus extras), 3 departments forming a 2-level tree, 2 groups, the system roles `admin` / `designer` / `viewer`, and RoleAssignments matching each persona's expected role set. Re-running the fixture on a non-empty DB SHALL be a no-op (idempotent on `User.email` uniqueness).

#### Scenario: Fresh DB seeded
- **WHEN** the fixture runs on an empty DB
- **THEN** querying `User` returns at least 6 rows (one per persona) plus extras for org-chart hierarchy, with proper manager_id linkages so `submitter.manager` resolves
- **AND** `Department` returns 3 rows with parent_id forming a tree
- **AND** RoleAssignments exist such that `admin` persona has the admin role, etc.

#### Scenario: Re-running on populated DB is no-op
- **WHEN** the fixture runs a second time
- **THEN** no duplicate User rows are inserted; no exception is thrown

### Requirement: Frontend stores JWT and switches via dev-login

The wizard frontend SHALL store the active JWT in `localStorage.bpm_jwt`, attach it to every API request via `apiFetch`, and on switching persona via `RoleSwitcher` SHALL call `POST /api/dev/login` with the new persona_code, replacing the stored JWT on success.

#### Scenario: RoleSwitcher mints new JWT
- **WHEN** the user picks "Manager" in the RoleSwitcher dropdown
- **THEN** the frontend POSTs to `/api/dev/login` with `persona_code: "manager"`
- **AND** the returned token replaces `localStorage.bpm_jwt`
- **AND** subsequent API calls carry the new token

#### Scenario: 401 clears token and redirects to login
- **WHEN** any API call returns 401
- **THEN** the frontend clears `localStorage.bpm_jwt` and surfaces the dev-login UI (or in prod, the IdP redirect)
