# Design notes

## 1. Why Entra ID specifically (not generic LDAP)

Target customer: Taiwan SME (50-300 employees). Entra ID dominates this segment (M365 + Teams adoption). LDAP / on-prem AD is rarer; if a customer needs it, we add later (or use a tunnel like Microsoft Entra Connect to bridge AD → Entra → BPM).

OIDC SSO already integrates with Entra for login (`add-sso-oidc`). HR sync extends that integration to data plane.

## 2. Permissions required at Entra app registration

For the BPM app's Service Principal:
- `User.Read.All` (read all users in directory) - Application permission
- `Group.Read.All` (when IncludeGroups = true)
- `Directory.Read.All` (alternative; coarser; some setups prefer)

These are admin-consent permissions — customer's IT admin grants once during setup.

## 3. Microsoft Graph SDK vs raw HTTP

Use the official `Microsoft.Graph` NuGet — handles auth, paging, rate limiting, retries on 429. Avoids reinventing token management.

Cost: 5 MB binary; acceptable.

## 4. Sync interval

Default 6 hours = 4 syncs per day. Reasonable for SME where org changes are infrequent (a few hires per week).

Customer can configure 1-24 hour interval. Setting 0 = manual only (pull when admin triggers).

## 5. Manager resolution via Graph

Entra User's `manager` relationship returns the manager's User object. Two approaches:

- **Lazy**: walk per-user manager → 1 Graph call per user. Slow; 100 users = 100 round trips.
- **Eager**: bulk fetch via $expand=manager → 1 paged call. Fast.

Use eager. `GET /users?$select=id,mail,displayName,jobTitle,department,accountEnabled&$expand=manager($select=id,mail)`

## 6. Entra group → BPM Group mapping

Entra Group's ObjectId is unique. BPM Group's `Code` is human-friendly. Mapping:

- BPM `Code` = Entra Group `mailNickname` if available, else the displayName slugified
- Store Entra ObjectId in BPM Group's `attributes` JSON for round-trip

Membership sync: full pull (all members) per group; replace BPM GroupMember rows for that group.

## 7. Concurrency

Two sync runs (scheduled + on-demand) on the same config simultaneously? Use a per-config lock row in EntraSyncConfiguration: `IsLocked = true` while running; refuses concurrent starts.

## 8. Open questions

- **Photo sync**: Entra has user photos; BPM could store as a User attribute file. Defer; nice-to-have.
- **Custom attribute sync**: Entra has extensible attributes. We push everything we don't recognize into User.attributes JSON; admin can pick which to surface in BPM UI.
- **Soft-delete via accountEnabled=false**: Entra users can be disabled; mirror to IsActive. No hard-delete on our side from Entra; if Entra hard-deletes a user, our user remains (audit trail) — this is a feature.
- **Permissions audit**: log every Graph call to AuditEvent (Category = HrSync) for traceability.
