## ADDED Requirements

### Requirement: BPM JWT shape unified across dev-login and SSO

The BPM-issued JWT SHALL carry the same claim shape regardless of whether it was minted via dev-login (existing) or SSO callback (new). Required claims: `sub` (User id), `email`, `name`, `tenant_id`, `roles[]`, `iat`, `exp`. The token SHALL be signed HS256 with `BPM_JWT_SECRET`.

#### Scenario: Dev-login token matches SSO token shape

- **WHEN** a dev-login mints a JWT for Wilson
- **AND** SSO callback also mints a JWT for Wilson
- **THEN** both tokens carry the same set of claims; the receiving API treats them identically

### Requirement: BPM_AUTH_MODE controls dev-login availability

`BPM_AUTH_MODE` env value SHALL gate dev-login endpoint availability:

- `dev` — dev-login enabled
- `prod` — dev-login returns 404
- `dev_with_sso` — dev-login enabled alongside SSO

#### Scenario: Prod hides dev-login

- **GIVEN** BPM_AUTH_MODE=prod
- **WHEN** a client calls POST /api/dev/login
- **THEN** 404
