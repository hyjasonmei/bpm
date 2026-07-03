# Plan — AI Kitchen Cook phase: chef ↔ admin via MCP

End-to-end design for moving the Cook tab from FE-only mock to a real
chef ↔ admin loop. Decision summary (locked 2026-05-28 via TG):

- **Chef stays manually launched** by 開發者. No auto-spawn.
- **chef ↔ admin talks MCP**, not raw HTTP. The MCP server is hosted
  inside bpm-admin-svc itself (same process / port / DI container).
- **chef sessions are one-shot.** No polling loop. When chef hits a
  blocker it posts the question, transitions the flow to `OnHold`, and
  the session exits. 開發者 re-launches chef after user replies; the new
  session re-fetches messages and continues.
- **Simulate buttons share the same backend.** The "demo" controls in
  CookPanel call the same chef HTTP endpoints (with a special demo
  bearer) so admin-ui is always the single source of truth — no
  FE-only message store.
- **flowId travels in the bundle** at `manifest.json.flowId`. chef
  reads it to address admin-svc.

---

## 1. Architecture

```
┌───────────────────────────┐         ┌───────────────────────────┐
│ Claude Code (chef skill)  │         │ bpm-admin-svc (one process)│
│                           │   MCP   │                           │
│  ┌─────────────────────┐  │ HTTP    │  ┌─────────────────────┐  │
│  │ MCP client          │◄─┼────────►│  │ MCP server          │  │
│  └─────────────────────┘  │   /mcp  │  │ (MapMcp via SDK)    │  │
│                           │         │  └────────┬────────────┘  │
│  reads bundle/spec.json   │         │           │               │
│  writes code → worktree   │         │           │ (shared DI)   │
│  branch (開發者 pushes)    │         │           │               │
└───────────────────────────┘         │  ┌────────▼────────────┐  │
                                       │  │ Application layer:  │  │
┌───────────────────────────┐         │  │  FlowLifecycleSvc   │  │
│ bpm-admin-ui              │  HTTP   │  │  FlowChatService    │  │
│ (Alice's browser)         │◄────────►│  │  (new)              │  │
│  /api/flows/*  /api/chat  │         │  └────────┬────────────┘  │
└───────────────────────────┘         │           │               │
                                       │  ┌────────▼────────────┐  │
                                       │  │ Persistence:        │  │
                                       │  │  Admin_Flow         │  │
                                       │  │  Admin_FlowChatMsg  │  │
                                       │  │  (new)              │  │
                                       │  └─────────────────────┘  │
                                       └───────────────────────────┘
```

Two transports talk to the same Application services:

- **HTTP** for admin-ui (existing) — user JWT auth
- **MCP** for chef Claude Code — chef-token auth via HTTP header

Both end up hitting the same `FlowLifecycleService` / `FlowChatService`
in the Application layer, so state stays consistent regardless of who
moves it.

---

## 2. Data — `Admin_FlowChatMessage`

New EF entity owned by admin-svc; bpm-svc never reads it.

```csharp
public class FlowChatMessage : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public FlowChatSender Sender { get; set; }      // User | Chef | System
    public FlowChatKind Kind { get; set; }          // see enum below
    public string Content { get; set; } = "";
    /// <summary>Free-form JSON; chef posts e.g. { branch, fileCount, testsPassing }.</summary>
    public string? ArtifactsJson { get; set; }
    public string? Version { get; set; }            // "V1.0", "V1.1" — only on completion messages
    public DateTime CreatedAt { get; set; }
    public Guid? AuthorUserId { get; set; }         // null for chef / system rows
    public DateTime? DeletedAt { get; set; }
}

public enum FlowChatSender { User = 0, Chef = 1, System = 2 }

public enum FlowChatKind
{
    Memo = 0,       // chef → progress update ("Domain layer done")
    Question = 1,   // chef → blocker; pairs with state transition to OnHold
    Completion = 2, // chef → "cook done" + artifacts
    Reply = 3,      // user → reply to a question
    Issue = 4,      // user → opens an issue after Committed
    System = 5,     // automatic state-transition notes
}
```

Table is on the admin DB (no SharedX exposure — bpm-svc has no business
reading it).

### Flow table additions

Add to `Bpm.Admin.Domain.Flows.Flow`:

```csharp
public DateTime? LastChefHeartbeatAt { get; set; }
```

Updated every time a chef API call touches the flow. The list page can
show "chef stalled?" when `Cooking` && `now - LastChefHeartbeatAt > 30min`.

---

## 3. State machine — chef-driven transitions

`FlowLifecycleService` gains two chef-side methods that mirror the
existing user-side ones:

| Existing user-side | New chef-side | Allowed from → To |
|---|---|---|
| `SubmitAsync` (user submits) | — | Draft → Submitted |
| — | `ChefAcceptAsync` | Submitted → Cooking |
| `OnHoldFromChefAsync` (already chef-driven) | (reuse, takes `question`) | Cooking → OnHold |
| `ResumeAsync` (user resume from OnHold) | `ChefResumeAsync` (chef self-resume after fetching user reply) | OnHold → Cooking |
| — | `ChefCommitAsync` | Cooking → Committed |

All transitions audit with actor = "chef" (no userId) so the audit log
distinguishes chef-driven changes from user-driven ones.

A new `ChefStallResetAsync` lets admin-ui drop a stalled `Cooking`
flow back to `Submitted` (manual recovery; not chef-driven, requires
user JWT).

---

## 4. Chef token auth

Two auth schemes coexist in admin-svc:

- **Existing `Bearer`** → user JWT (login flow, RBAC, etc.)
- **New `ChefBearer`** → static token `Bpm:Chef:Token` from appsettings

`/api/chef/*` and `/mcp` require `ChefBearer`. `/api/*` (everything else)
requires user JWT.

Implementation: an `AuthenticationHandler<ChefAuthenticationOptions>`
that checks `Authorization: Bearer <chef-token>` against the configured
value and sets a synthetic `ClaimsPrincipal` with role `Chef`. Endpoints
declare `[Authorize(AuthenticationSchemes = "ChefBearer", Roles = "Chef")]`.

POC: single static token. Future PR can mint per-flow tokens at
`SubmitAsync` time and expire them on `Committed/Cancel`.

---

## 5. Chef HTTP endpoints (mirrored by MCP tools)

All under `/api/chef`, all `[Authorize(ChefBearer)]`:

| Verb | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/chef/flows/{flowId}` | — | flow detail (`state`, `flowCode`, `version`, `specJson`) |
| GET | `/api/chef/flows/{flowId}/bundle` | — | bundle zip (re-builds if missing) |
| GET | `/api/chef/flows/{flowId}/messages?since={ts}` | — | message thread (full history; cursor optional) |
| POST | `/api/chef/flows/{flowId}/messages` | `{ kind, content, artifactsJson?, version? }` | created `FlowChatMessage` |
| POST | `/api/chef/flows/{flowId}/transition` | `{ target: 'Cooking' \| 'OnHold' \| 'Committed' \| 'Resume', question?, reason? }` | updated flow + appended system chat row |

Every POST also updates `LastChefHeartbeatAt = utcNow`.

### MCP tool surface (1:1 with HTTP)

Tools registered via `WithToolsFromAssembly`:

- `chef_get_flow(flowId)` → flow detail
- `chef_get_messages(flowId, since?)` → thread
- `chef_post_message(flowId, kind, content, artifactsJson?, version?)` → ack
- `chef_transition(flowId, target, question?, reason?)` → flow state + ack
- `chef_download_bundle(flowId)` → returns base64 zip (chef writes locally)

Same Application services power both — controllers and MCP tools are
both thin façades.

---

## 6. Bundle changes

`manifest.json` adds:

```json
{
  "bundleSchemaVersion": 1,
  "flowId": "12bc05d9-b4aa-4984-9f8c-83b52bb76f31",
  "flowCode": "LEAVE",
  "flowVersion": 1,
  ...
}
```

chef reads `manifest.flowId` on session start and uses it for every MCP
tool call. `BundleBuilder` populates the field from `Flow.Id`.

---

## 7. User-side endpoints (still user-JWT)

CookPanel needs a single new user endpoint:

| Verb | Path | Body | Returns | Note |
|---|---|---|---|---|
| POST | `/api/flows/{flowId}/chat-reply` | `{ kind: 'reply' \| 'issue'; content }` | created msg | gated by `flow.state in {OnHold, Committed}` |

`GET /api/flows/{flowId}/messages` (user-side) reuses the chef-side
shape but is user-JWT and respects flow visibility.

---

## 8. Admin-ui CookPanel rewrites

Replace FE-only message store with fetched-from-admin-svc store:

1. On mount, `useCookMessages(flowId)` fetches the thread + sets up
   30s polling.
2. Simulate buttons (`Chef picks up`, `Chef asks question`, etc.) POST
   to `/api/chef/*` with a hard-coded demo token (admin-ui has it in
   env / served by admin-svc as a hint — POC compromise).
   - Long-term: simulate buttons go away once real chef sessions take
     over. The shared backend means demo and real cooks render identical
     timelines.
3. User reply textarea POSTs `/api/flows/{id}/chat-reply` (user-JWT).
4. List page row gets a "🟠 chef stalled" pill when
   `state == Cooking && now - lastChefHeartbeatAt > 30min`.
5. CookPanel header shows "chef offline" when state is `OnHold`, plus
   a one-line copy-paste `BPM_FLOW_ID=<id> chef-resume` reminder.

---

## 9. Chef skill updates

`chef/skill/SKILL.md` adds §6 "Talking to admin (MCP)":

- **Env vars chef expects:** none from caller. Chef reads
  `bundle/manifest.json.flowId` and the MCP connection is configured
  in `.mcp.json` (Claude Code config) with the chef token.
- **Session lifecycle (one-shot):**
  1. start → read bundle → `chef_get_flow(flowId)` → if state ≠
     Submitted, also `chef_get_messages` to see why we're resuming
  2. `chef_transition(target='Cooking')` + `chef_post_message(kind='memo')`
     "Picking up; will scaffold Domain → Application → Persistence → Api → UI"
  3. For each layer completed: `chef_post_message(kind='memo')` "Layer X done"
  4. If blocker: `chef_transition(target='OnHold', question='...')` +
     **exit session**. Do not poll.
  5. On completion: `chef_transition(target='Committed')` +
     `chef_post_message(kind='completion', artifactsJson={branch,
     fileCount, testsPassing})`. **Exit.**
- **Resume after OnHold:** new chef session → `chef_get_messages` →
  see user's reply → `chef_transition(target='Resume')` → continue.
- **5 milestone memos:** Domain / Application / Persistence / Api /
  UI. Memo per layer end.
- **No artifacts in DB beyond metadata.** Real diff lives on the
  worktree branch; chef just posts the branch name and counts.

---

## 10. PR breakdown

| PR | Scope | Effort |
|---|---|---|
| **K0** | Design doc (this) + commit | done |
| **K1** | Admin-svc: `FlowChatMessage` entity + migration + chef HTTP endpoints + ChefBearer auth + ChefAccept/Commit/Resume/StallReset + heartbeat | 1 day |
| **K2** | Admin-svc: MCP server (ModelContextProtocol.AspNetCore) + 5 MCP tools mirroring K1 endpoints + bundle manifest.flowId field + builder wiring | 0.5 day |
| **K3** | Admin-ui: CookPanel reads/writes admin-svc; simulate buttons go through chef endpoints; user-reply endpoint; stall indicator on list; resume hint | 0.5 day |
| **K4** | chef/skill/SKILL.md §6 + `.mcp.json` example for chef sessions | 0.25 day |

Total ~2.25 days.

---

## 11. Open questions (deferred)

- **Per-flow chef tokens** instead of one static token — better security,
  more moving parts. Skip for POC; revisit when more than one chef ever.
- **MCP authn helper for Claude Code** — `.mcp.json` Bearer header
  syntax verified against latest SDK at implementation time.
- **Bundle re-download on resume** — chef session 2 may not have the
  bundle on disk. `chef_download_bundle` covers it; verify the rebuild
  path doesn't break audit / hashing assumptions.
- **Stall TTL configurable** (default 30 min; make `Bpm:Chef:StallTtl`).
- **Audit row for chef-driven transitions** — actor field carries
  `"chef:<flowId>"` so audit history reads sensibly.

---

## 12. Acceptance criteria (POC)

1. 開發者 runs `dotnet run` on admin-svc → both API and MCP up on :5266.
2. From admin-ui, Submit a Draft → state moves to `Submitted`.
3. 開發者 starts a chef Claude Code session in a worktree pointed at the
   downloaded bundle. chef MCP client connects.
4. Chef calls `chef_get_flow` → reads spec → calls
   `chef_transition('Cooking')` → admin-ui CookPanel **automatically
   reflects** the Cooking state + chef's memo without refresh (poll
   picks it up within 30s).
5. Chef writes some code, posts 2 memos, then a Completion message.
   State moves to `Committed`. admin-ui shows the timeline.
6. 開發者 opens an issue from admin-ui (user reply). New chef session
   sees it via `chef_get_messages`, resumes, posts a fix memo.
7. Simulate "Chef picks up" button on a fresh draft moves state +
   appends a chef-side message via the same backend (no FE-only state).
8. Killing chef mid-Cooking → list page shows "chef stalled" indicator
   30 min later.
