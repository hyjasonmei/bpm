# 並簽 P1 Implementation Plan

> Spec: `docs/specs/2026-07-02-parallel-approval-design.md`. Follows Model B (per-flow hand-written) + one single-responsibility shared primitive.
> **Commits:** this repo pushes via GitKraken (Jason). "Checkpoint" steps below mean *build + tests green*; commits are batched by Jason, not run here.

**Goal:** Ship the runtime + a reference demo flow so a case can require concurrent multi-approver sign-off (threshold M/N, any-reject→all-reject) with multi-node BPMN highlight.

**Architecture:** A shared `ParallelApprovalGroup`/`Slot` primitive (bpm-svc runtime tables + `IParallelApprovalService`) does fork/join/reject + is queryable for the inbox. Each flow's hand-written state machine opens a group on entering its parallel step and advances/rejects on the group result. bpm-ui `BpmnView` lights multiple nodes; a case-detail component shows the approval checklist.

**Tech Stack:** .NET 10 Clean Arch (Domain/Application/Persistence/Api), EF Core (Postgres/SQLite), xUnit; React 18 + Vite + Tailwind + bpmn-js.

---

## File Structure

**Shared primitive (lead):**
- `bpm-svc/src/Domain/Parallel/ParallelApprovalGroup.cs` — group entity + `ParallelGroupStatus` enum
- `bpm-svc/src/Domain/Parallel/ParallelApprovalSlot.cs` — slot entity + `SlotDecision` enum
- `bpm-svc/src/Application/Parallel/IParallelApprovalService.cs` — interface + DTOs
- `bpm-svc/src/Persistence/Parallel/ParallelApprovalService.cs` — impl
- `bpm-svc/src/Persistence/Parallel/ParallelApprovalGroupConfiguration.cs`, `ParallelApprovalSlotConfiguration.cs` — EF mapping
- `bpm-svc/src/Persistence/Migrations/<ts>_ParallelApproval.cs` — migration (+ snapshot)
- `bpm-svc/tests/Bpm.Tests/Parallel/ParallelApprovalServiceTests.cs`

**Frontend primitives (lead):**
- `bpm-ui/src/components/BpmnView.tsx` — `currentNode` → `currentNodes[]` + rejected/skipped markers
- `bpm-ui/src/components/ParallelApprovalPanel.tsx` — case-detail checklist
- `bpm-ui/src/styles` (or BpmnView CSS) — `.bpm-rejected`, `.bpm-skipped`

**Demo flow CONTRACT_REVIEW (chef boundary; copy LEAVE V1 shape):**
- `bpm-svc/src/Domain/Features/CONTRACT_REVIEW/V1/*`
- `bpm-svc/src/Application/Features/CONTRACT_REVIEW/V1/*`
- `bpm-svc/src/Persistence/Features/CONTRACT_REVIEW/V1/*` + migration
- `bpm-svc/src/Api/Features/CONTRACT_REVIEW/V1/*`
- `bpm-ui/src/features/CONTRACT_REVIEW/V1/*` (form, case-detail, manifest.ts, .bpmn.xml)
- `bpm-svc/tests/Bpm.Tests/Features/CONTRACT_REVIEW/V1/*`

---

## Task 1: ParallelApproval domain entities

**Files:** Create `bpm-svc/src/Domain/Parallel/ParallelApprovalGroup.cs`, `ParallelApprovalSlot.cs`

- [ ] **Step 1: Write entities + enums**

```csharp
// ParallelApprovalGroup.cs
namespace Bpm.Domain.Parallel;

public enum ParallelGroupStatus { Open = 0, Approved = 1, Rejected = 2 }

/// <summary>One parallel-approval gateway instance for one case.</summary>
public class ParallelApprovalGroup
{
    public Guid Id { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public int FlowVersion { get; set; }
    public Guid CaseId { get; set; }
    public string GatewayNodeId { get; set; } = string.Empty; // spec fork node id
    public int Threshold { get; set; }      // M in M/N (N/N = AND, 1/N = OR)
    public int TotalSlots { get; set; }     // N
    public ParallelGroupStatus Status { get; set; } = ParallelGroupStatus.Open;
    public DateTime OpenedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public List<ParallelApprovalSlot> Slots { get; set; } = new();
}
```

```csharp
// ParallelApprovalSlot.cs
namespace Bpm.Domain.Parallel;

public enum SlotDecision { Pending = 0, Approved = 1, Rejected = 2, Skipped = 3 }

/// <summary>One branch of a parallel gateway = one approver's slot.</summary>
public class ParallelApprovalSlot
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string NodeId { get; set; } = string.Empty;  // this branch's user-task node id (for BPMN highlight)
    public string? AssigneeRoleCode { get; set; }        // role queue: any holder (or delegate) may act
    public Guid? AssigneeUserId { get; set; }            // or a specific user
    public SlotDecision Decision { get; set; } = SlotDecision.Pending;
    public string? Comment { get; set; }
    public Guid? DecisionByUserId { get; set; }
    public DateTime? DecisionAt { get; set; }
}
```

- [ ] **Step 2: Checkpoint** — `dotnet build src/Domain/Domain.csproj` green.

---

## Task 2: EF config + DbSets + migration

**Files:** Create `bpm-svc/src/Persistence/Parallel/ParallelApprovalGroupConfiguration.cs`, `ParallelApprovalSlotConfiguration.cs`; Modify `bpm-svc/src/Persistence/AppDbContext.cs`

- [ ] **Step 1: Configurations** (mirror an existing feature configuration for conventions — string lengths, enum-as-int via default). Group: PK Id; index `(FlowCode, CaseId, GatewayNodeId)`. Slot: PK Id; FK GroupId → Group.Slots (cascade); index `(AssigneeRoleCode, Decision)` and `(AssigneeUserId, Decision)` for inbox queries.

```csharp
// ParallelApprovalGroupConfiguration.cs
using Bpm.Domain.Parallel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Bpm.Persistence.Parallel;

public sealed class ParallelApprovalGroupConfiguration : IEntityTypeConfiguration<ParallelApprovalGroup>
{
    public void Configure(EntityTypeBuilder<ParallelApprovalGroup> b)
    {
        b.ToTable("ParallelApprovalGroups");
        b.HasKey(x => x.Id);
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.GatewayNodeId).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.FlowCode, x.CaseId, x.GatewayNodeId });
        b.HasMany(x => x.Slots).WithOne().HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// ParallelApprovalSlotConfiguration.cs
using Bpm.Domain.Parallel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Bpm.Persistence.Parallel;

public sealed class ParallelApprovalSlotConfiguration : IEntityTypeConfiguration<ParallelApprovalSlot>
{
    public void Configure(EntityTypeBuilder<ParallelApprovalSlot> b)
    {
        b.ToTable("ParallelApprovalSlots");
        b.HasKey(x => x.Id);
        b.Property(x => x.NodeId).HasMaxLength(128).IsRequired();
        b.Property(x => x.AssigneeRoleCode).HasMaxLength(64);
        b.HasIndex(x => new { x.AssigneeRoleCode, x.Decision });
        b.HasIndex(x => new { x.AssigneeUserId, x.Decision });
    }
}
```

- [ ] **Step 2: Add DbSets** to `AppDbContext` (follow existing DbSet style):
```csharp
public DbSet<Bpm.Domain.Parallel.ParallelApprovalGroup> ParallelApprovalGroups => Set<Bpm.Domain.Parallel.ParallelApprovalGroup>();
public DbSet<Bpm.Domain.Parallel.ParallelApprovalSlot> ParallelApprovalSlots => Set<Bpm.Domain.Parallel.ParallelApprovalSlot>();
```
Confirm `ApplyConfigurationsFromAssembly` already picks up new `IEntityTypeConfiguration`s (it does if the context uses it; else register both explicitly).

- [ ] **Step 3: Migration** (postgres provider, per project convention):
```bash
cd bpm-svc
BPM_DB_PROVIDER=postgres dotnet ef migrations add ParallelApproval -p src/Persistence -s src/Api
```
Expected: new migration + `AppDbContextModelSnapshot` updated with both tables.

- [ ] **Step 4: Checkpoint** — `dotnet build src/Api/Api.csproj` green; `BPM_DB_PROVIDER=postgres dotnet ef migrations has-pending-model-changes -p src/Persistence -s src/Api` → "No changes".

---

## Task 3: IParallelApprovalService interface + DTOs

**Files:** Create `bpm-svc/src/Application/Parallel/IParallelApprovalService.cs`

- [ ] **Step 1: Interface + result types**

```csharp
using Bpm.Domain.Parallel;
namespace Bpm.Application.Parallel;

public sealed record SlotSpec(string NodeId, string? RoleCode, Guid? UserId);

/// Result of a decision: the group's post-decision status, so the caller's
/// flow state machine can advance (Approved), reject (Rejected), or wait (Open).
public sealed record DecisionResult(ParallelGroupStatus GroupStatus, ParallelApprovalGroup Group);

public interface IParallelApprovalService
{
    /// Open a parallel gateway: create the group + one Pending slot per branch.
    Task<ParallelApprovalGroup> OpenAsync(
        string flowCode, int flowVersion, Guid caseId, string gatewayNodeId,
        IReadOnlyList<SlotSpec> slots, int threshold, CancellationToken ct);

    /// Record one approver's decision on their slot; recompute the group.
    /// Throws NotFoundException (slot), ConflictException (group not Open / slot not Pending),
    /// UnauthorizedException (actor may not act on this slot).
    Task<DecisionResult> DecideAsync(
        Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct);

    /// Load the group (+ slots) for a case's gateway, for display / inbox.
    Task<ParallelApprovalGroup?> GetAsync(Guid caseId, string gatewayNodeId, CancellationToken ct);

    /// Slots still Pending that this user (directly or via role/delegation) may act on.
    Task<IReadOnlyList<ParallelApprovalSlot>> FindPendingForUserAsync(
        string flowCode, Guid userId, IReadOnlyCollection<string> roleCodes, CancellationToken ct);
}
```

- [ ] **Step 2: Checkpoint** — `dotnet build src/Application/Application.csproj` green.

---

## Task 4: ParallelApprovalService impl + unit tests (TDD — the core logic)

**Files:** Create `bpm-svc/src/Persistence/Parallel/ParallelApprovalService.cs`; Test `bpm-svc/tests/Bpm.Tests/Parallel/ParallelApprovalServiceTests.cs`

Join rule (from spec §4): after each decision recompute —
1. any slot `Rejected` → group `Rejected`; all remaining `Pending` → `Skipped`.
2. else `Approved` count ≥ `Threshold` → group `Approved`; remaining `Pending` → `Skipped`.
3. else stay `Open`.

- [ ] **Step 1: Write failing tests** (in-memory SQLite like existing tests; hand-create schema or use EnsureCreated on a test context with the two tables).

```csharp
using Bpm.Domain.Parallel;
using Bpm.Application.Parallel;
using Xunit;
// ... test fixture builds an AppDbContext on sqlite in-memory (mirror existing Parallel-free feature tests) ...

public class ParallelApprovalServiceTests
{
    // helper: OpenAsync with 3 role slots, threshold t
    // Actor auth stub: IActorAuthorizer that returns CanAct=true for the tested users.

    [Fact] public async Task AllApproved_threshold_N_of_N_resolves_Approved() {
        // open 3 slots threshold 3; approve all 3 → group.Approved, no Skipped
    }
    [Fact] public async Task Threshold_M_of_N_resolves_and_skips_remaining() {
        // open 5 slots threshold 3; approve 3 → group.Approved; other 2 → Skipped
    }
    [Fact] public async Task Any_reject_rejects_group_and_skips_rest() {
        // open 3 threshold 3; slot#1 reject → group.Rejected; other 2 → Skipped
    }
    [Fact] public async Task Decide_on_already_resolved_group_throws_Conflict() { }
    [Fact] public async Task Unauthorized_actor_throws() { }
    [Fact] public async Task FindPendingForUser_matches_role_and_user_slots_only_when_Pending() { }
}
```

- [ ] **Step 2: Run tests → FAIL** (`dotnet test --filter ParallelApprovalServiceTests`). Expected: compile error / red (no impl).

- [ ] **Step 3: Implement service**

```csharp
using Bpm.Application.Common.Authorization;   // IActorAuthorizer
using Bpm.Application.Common.Abstractions;     // IClock
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Parallel;
using Bpm.Domain.Parallel;
using Bpm.Persistence;                          // AppDbContext
using Microsoft.EntityFrameworkCore;
namespace Bpm.Persistence.Parallel;

public sealed class ParallelApprovalService(AppDbContext db, IClock clock, IActorAuthorizer auth)
    : IParallelApprovalService
{
    public async Task<ParallelApprovalGroup> OpenAsync(string flowCode, int flowVersion, Guid caseId,
        string gatewayNodeId, IReadOnlyList<SlotSpec> slots, int threshold, CancellationToken ct)
    {
        var g = new ParallelApprovalGroup {
            Id = Guid.NewGuid(), FlowCode = flowCode, FlowVersion = flowVersion, CaseId = caseId,
            GatewayNodeId = gatewayNodeId, Threshold = threshold, TotalSlots = slots.Count,
            Status = ParallelGroupStatus.Open, OpenedAt = clock.UtcNow,
            Slots = slots.Select(s => new ParallelApprovalSlot {
                Id = Guid.NewGuid(), NodeId = s.NodeId, AssigneeRoleCode = s.RoleCode,
                AssigneeUserId = s.UserId, Decision = SlotDecision.Pending }).ToList(),
        };
        db.ParallelApprovalGroups.Add(g);
        await db.SaveChangesAsync(ct);
        return g;
    }

    public async Task<DecisionResult> DecideAsync(Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var slot = await db.ParallelApprovalSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct)
                   ?? throw new NotFoundException(nameof(ParallelApprovalSlot), slotId);
        var g = await db.ParallelApprovalGroups.Include(x => x.Slots)
                    .FirstAsync(x => x.Id == slot.GroupId, ct);
        if (g.Status != ParallelGroupStatus.Open) throw new ConflictException("parallel group already resolved");
        var live = g.Slots.First(s => s.Id == slotId);
        if (live.Decision != SlotDecision.Pending) throw new ConflictException("slot already decided");

        // authorize: role-aware (+ delegation) via existing IActorAuthorizer
        var ok = await auth.CanActAsync(actorUserId, live.AssigneeUserId, live.AssigneeRoleCode, ct);
        if (!ok) throw new UnauthorizedException("actor may not act on this slot");

        live.Decision = approve ? SlotDecision.Approved : SlotDecision.Rejected;
        live.Comment = comment; live.DecisionByUserId = actorUserId; live.DecisionAt = clock.UtcNow;

        Recompute(g);
        await db.SaveChangesAsync(ct);
        return new DecisionResult(g.Status, g);
    }

    private void Recompute(ParallelApprovalGroup g)
    {
        if (g.Slots.Any(s => s.Decision == SlotDecision.Rejected)) {
            g.Status = ParallelGroupStatus.Rejected;
        } else if (g.Slots.Count(s => s.Decision == SlotDecision.Approved) >= g.Threshold) {
            g.Status = ParallelGroupStatus.Approved;
        } else return; // still Open
        foreach (var s in g.Slots.Where(s => s.Decision == SlotDecision.Pending)) s.Decision = SlotDecision.Skipped;
        g.ResolvedAt = clock.UtcNow;
    }

    public Task<ParallelApprovalGroup?> GetAsync(Guid caseId, string gatewayNodeId, CancellationToken ct) =>
        db.ParallelApprovalGroups.Include(x => x.Slots)
          .FirstOrDefaultAsync(x => x.CaseId == caseId && x.GatewayNodeId == gatewayNodeId, ct);

    public async Task<IReadOnlyList<ParallelApprovalSlot>> FindPendingForUserAsync(string flowCode, Guid userId,
        IReadOnlyCollection<string> roleCodes, CancellationToken ct)
    {
        var q = from s in db.ParallelApprovalSlots
                join g in db.ParallelApprovalGroups on s.GroupId equals g.Id
                where g.FlowCode == flowCode && g.Status == ParallelGroupStatus.Open
                      && s.Decision == SlotDecision.Pending
                      && (s.AssigneeUserId == userId
                          || (s.AssigneeRoleCode != null && roleCodes.Contains(s.AssigneeRoleCode)))
                select s;
        return await q.ToListAsync(ct);
    }
}
```

> Note: confirm `IActorAuthorizer.CanActAsync(userId, assigneeUserId?, roleCode?, ct)` signature (added in the shared-role-queue work). If the overload differs, adapt the call and the auth stub in tests to match. Confirm `UnauthorizedException` exists in `Bpm.Application.Common.Exceptions`; if not, use the project's 403 exception type.

- [ ] **Step 4: Register in DI** — in `bpm-svc/src/Api/Program.cs` add `builder.Services.AddScoped<IParallelApprovalService, ParallelApprovalService>();` (near other scoped service registrations).

- [ ] **Step 5: Run tests → PASS.** `dotnet test --filter ParallelApprovalServiceTests`.

- [ ] **Step 6: Checkpoint** — build + these tests green.

---

## Task 5: BpmnView multi-node highlight

**Files:** Modify `bpm-ui/src/components/BpmnView.tsx`; add CSS for `.bpm-rejected`, `.bpm-skipped`.

- [ ] **Step 1: Widen the API** — replace `currentNode?: string | null` with `currentNodes?: string[]`, add `rejectedNodes?: string[]`, `skippedNodes?: string[]` (keep `completedNodes`). In the import branch, after `completedNodes`:
```tsx
for (const id of completedNodes ?? []) canvas.addMarker(id, 'bpm-completed')
for (const id of currentNodes ?? []) canvas.addMarker(id, 'bpm-active')
for (const id of rejectedNodes ?? []) canvas.addMarker(id, 'bpm-rejected')
for (const id of skippedNodes ?? []) canvas.addMarker(id, 'bpm-skipped')
```
Update the props destructure + `BpmnViewProps`. Grep callers of `currentNode=` and migrate them to `currentNodes={x ? [x] : []}` (keep sequential flows working: single active node → array of one).

- [ ] **Step 2: CSS** — where `.bpm-completed`/`.bpm-active` styles live (grep `bpm-active`), add:
```css
.bpm-rejected .djs-visual > :nth-child(1) { stroke: #dc2626 !important; fill: #fee2e2 !important; }
.bpm-skipped  .djs-visual > :nth-child(1) { stroke: #cbd5e1 !important; fill: #f1f5f9 !important; stroke-dasharray: 4 3 !important; opacity: .7; }
```
(match the existing selector shape used by `.bpm-active`.)

- [ ] **Step 3: Checkpoint** — `npx tsc -p tsconfig.app.json --noEmit` clean; existing sequential flow BPMN still highlights (manual boot check).

---

## Task 6: ParallelApprovalPanel (case-detail checklist)

**Files:** Create `bpm-ui/src/components/ParallelApprovalPanel.tsx`

- [ ] **Step 1: Component** — props: `{ policyLabel: string; approvedCount: number; total: number; threshold: number; slots: { role?: string; name: string; state: 'pending'|'approved'|'rejected'|'skipped'; comment?: string; at?: string }[] }`. Renders the policy line + progress bar (`approvedCount/threshold`) + one row per slot with status pill (colours matching the mockup: 🟡待簽/🟢已核准/🔴已退件/⚪略過). Pure presentational; data comes from the case-detail API (Task 11).

- [ ] **Step 2: Checkpoint** — tsc clean.

---

## Task 7: Inbox — surface parallel-pending cases

**Design:** Each flow's inbox provider already lists sequential-pending cases. For the parallel step, the provider also asks `IParallelApprovalService.FindPendingForUserAsync(flowCode, userId, roles)` and maps those slots' `CaseId`s into inbox rows. Resolved/skipped slots drop out automatically (only `Pending` matched).

- [ ] Implemented inside the CONTRACT_REVIEW inbox provider (Task 10). No shared inbox change needed beyond the service call. (If a second parallel flow later reuses it, keep the same pattern.)

---

## Task 8: CONTRACT_REVIEW domain (copy LEAVE V1 shape)

**Files:** Create `bpm-svc/src/Domain/Features/CONTRACT_REVIEW/V1/CONTRACT_REVIEW_V1_Case.cs` + `_CaseStatus.cs`

Flow: submitter files a contract → **parallel review (法務 LEGAL + 財務 FINANCE, threshold 2/2 = AND)** → Completed. Reject → Rejected.

- [ ] **Step 1:** `CONTRACT_REVIEW_V1_CaseStatus` enum: `PendingParallelReview = 0, Completed = 1, Rejected = 2`.
- [ ] **Step 2:** `CONTRACT_REVIEW_V1_Case`: business fields (`SubmitterUserId`, `Title`, `Counterparty`, `Amount`, `ContractFileId?`), workflow (`Status`, `ReviewGatewayNodeId` const value stored or fixed, `SubmittedAt`, `LastActivityAt`, `CompletedAt?`). No per-approver columns — those live in the parallel primitive. Include the gateway node id constant (e.g. `"gw_review"`).
- [ ] **Step 3: Checkpoint** — Domain builds.

---

## Task 9: CONTRACT_REVIEW persistence (copy LEAVE V1)

**Files:** `bpm-svc/src/Persistence/Features/CONTRACT_REVIEW/V1/CONTRACT_REVIEW_V1_CaseConfiguration.cs`; CaseStore impl; migration.

- [ ] **Step 1:** EF configuration for the case (mirror LEAVE's). Add DbSet.
- [ ] **Step 2:** `ICONTRACT_REVIEW_V1_CaseStore` (Application) + impl (Persistence): `Add`, `SaveChangesAsync`, `FindByIdAsync`, `FindMineAsync(userId)`. (Pending is served by the parallel service, so no role-based FindPending needed here — mine + byId suffice.)
- [ ] **Step 3: Migration** `BPM_DB_PROVIDER=postgres dotnet ef migrations add CONTRACT_REVIEW_V1 -p src/Persistence -s src/Api`.
- [ ] **Step 4: Checkpoint** — build + has-pending-model-changes clean.

---

## Task 10: CONTRACT_REVIEW state machine + inbox (the parallel wiring)

**Files:** `bpm-svc/src/Application/Features/CONTRACT_REVIEW/V1/CONTRACT_REVIEW_V1_Service.cs`, `_InboxProvider.cs`

- [ ] **Step 1: Service** (inject `ICONTRACT_REVIEW_V1_CaseStore`, `IParallelApprovalService`, `IClock`, `INotifyDispatcher`, `IPrincipalDirectory`):
  - `SubmitAsync(input)`: validate; create case `Status=PendingParallelReview`; then `parallel.OpenAsync("CONTRACT_REVIEW", 1, case.Id, "gw_review", slots, threshold: 2, ct)` where `slots = [ new SlotSpec("task_legal", "LEGAL", null), new SlotSpec("task_finance", "FINANCE", null) ]`; notify assignees.
  - `DecideAsync(slotId, actorUserId, approve, comment)`: call `parallel.DecideAsync(...)`; on `GroupStatus.Approved` → set case `Completed` + `CompletedAt`; on `Rejected` → case `Rejected`; on `Open` → no case change. Save + notify.
- [ ] **Step 2: InboxProvider** (`ITypedInboxProvider`):
  - `GetMineAsync(userId)`: `store.FindMineAsync` → rows.
  - `GetPendingAsync(userId)`: `var roles = await directory.GetRoleCodesForUserAsync(userId,ct); var slots = await parallel.FindPendingForUserAsync("CONTRACT_REVIEW", userId, roles, ct);` load those cases by `slots.Select(s=>s.CaseId)` and map to rows (Title shows contract + "並簽 x/2").
- [ ] **Step 3: Checkpoint** — Application builds.

---

## Task 11: CONTRACT_REVIEW API (controller + DTO + case detail)

**Files:** `bpm-svc/src/Api/Features/CONTRACT_REVIEW/V1/CONTRACT_REVIEW_V1_Controller.cs` (+ DTOs)

- [ ] **Step 1:** Endpoints (mirror LEAVE controller auth/JWT sub extraction):
  - `POST /api/contract-review/v1` → Submit.
  - `POST /api/contract-review/v1/{caseId}/slots/{slotId}/decision` `{approve, comment}` → `service.DecideAsync`.
  - `GET /api/contract-review/v1/{caseId}` → detail DTO: case fields + the parallel group (policy, threshold, total, approvedCount, slots[{nodeId, roleCode, resolvedName, state, comment, at}]) via `parallel.GetAsync(caseId,"gw_review")` + `directory` for names.
- [ ] **Step 2: Checkpoint** — Api builds; boot + curl submit→decide→detail returns group state.

---

## Task 12: CONTRACT_REVIEW UI (form, case-detail, manifest, bpmn.xml)

**Files:** `bpm-ui/src/features/CONTRACT_REVIEW/V1/*`

- [ ] **Step 1:** `CONTRACT_REVIEW_V1_Form.tsx` (copy a simple feature form; fields Title/Counterparty/Amount/file).
- [ ] **Step 2:** `CONTRACT_REVIEW_V1_CaseDetail.tsx` — renders `ParallelApprovalPanel` (Task 6) from the detail API; each pending slot the current user may act on shows 核准/退件 (ActionFooter styled confirm modal — per project convention, not window.confirm); a "檢視流程圖" button opens `BpmnView` with `currentNodes/completedNodes/rejectedNodes/skippedNodes` computed from slot states.
- [ ] **Step 3:** `.bpmn.xml` — Start → parallel gateway `gw_review` → `task_legal` + `task_finance` → join `gw_join` → End. Node ids MUST match the slot `NodeId`s + case `GatewayNodeId`.
- [ ] **Step 4:** `manifest.ts` — register CONTRACT_REVIEW V1 (feature registry auto-globs `features/*/V*/manifest.ts`).
- [ ] **Step 5: Checkpoint** — tsc clean; launcher lists the flow.

---

## Task 13: Seed + register/publish

- [ ] **Step 1:** Ensure LEGAL + FINANCE roles exist with holders in the admin seed (FINANCE exists; add a LEGAL role + a holder if missing — admin `Seeder.cs`). Confirm submitter persona has a manager path not needed here (no manager step).
- [ ] **Step 2:** Register + publish CONTRACT_REVIEW at runtime version (via the admin register-shipped / flow-codes path used by the Reset flow) so the launcher serves it.
- [ ] **Step 3: Checkpoint** — `/api/flow-registry` lists CONTRACT_REVIEW Published.

---

## Task 14: Integration + E2E

**Files:** `bpm-svc/tests/Bpm.Tests/Features/CONTRACT_REVIEW/V1/CONTRACT_REVIEW_V1_FlowTests.cs`

- [ ] **Step 1:** Integration (in-memory sqlite, mirror existing feature flow tests): submit → LEGAL approves (group Open, case still PendingParallelReview) → FINANCE approves (group Approved → case Completed). Second test: submit → LEGAL rejects → case Rejected, FINANCE slot Skipped, further decide throws Conflict.
- [ ] **Step 2:** Authorization test: a non-LEGAL/non-FINANCE user decide → 403.
- [ ] **Step 3:** Run all: `dotnet test`. Expected green.
- [ ] **Step 4: E2E (chrome + smoke):** boot stack; submit as employee; switch to LEGAL holder → approve; switch to FINANCE holder → approve → case Completed; open BPMN → both nodes 🟢, join passed. Reject variant. Verify on Postgres.

---

## Self-Review notes
- Spec coverage: §5 primitive → T1-4; §6 inbox → T7/T10; §7 BPMN+detail → T5/6/12; demo flow → T8-13; tests → T4/T14. AI Kitchen/chef-skill = P2/P3, out of this plan.
- Open confirmations flagged inline: `IActorAuthorizer.CanActAsync` overload, `UnauthorizedException` type, `AppDbContext` config-registration mechanism — verify against code at execution start (Task 4/2).
