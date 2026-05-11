# Design notes

## 1. Why hoist the wizard to a free-form designer

The wizard's 9 steps are great for first-time onboarding (linear, AI-led discovery). But mature flows need:

- Editing one step without re-walking all 9
- Side-by-side BPMN + properties panel (industry standard for designers)
- Save without "Go Live" semantics (drafts are normal)
- Diffing between versions

Hoisted: same components (StepFlow / StepForms / StepApprovers / StepNotify / StepDecisions) embedded in a free-form layout.

The wizard remains for new-flow creation; the designer is for ongoing maintenance. Both write to the same spec.json shape.

## 2. Simulator architecture

Simulator runs the same `IProcessRuntime` engine but in "dry mode":

- Wraps DbContext in a transaction that rolls back at the end (no writes persist)
- Uses an in-memory variant of `INotificationDispatcher` that records dispatches to a list rather than insert NotificationDelivery rows
- Uses an in-memory IFileStorageService that doesn't touch real storage
- Returns a structured trace: every state transition, gateway evaluation, recipient resolution, computed DueAt, etc.

UI consumes the trace and renders a node-by-node visualization.

This avoids duplicating the runtime logic. Single code path; "dry mode" is a runtime flag.

## 3. Admin intervention semantics

Each admin action writes a TaskHistory row with `actor_role = 'admin'` and a mandatory `reason`. This makes admin interventions auditable and distinguishes them from organic user actions.

Endpoints:
- `force-reassign`: pick a different user; original task Status = Cancelled with reason "AdminForceReassigned"; new task spawned
- `force-return`: send back to a chosen previous userTask; mirrors regular Return but with admin actor + reason
- `force-submit`: admin acts as the assignee; rare; produces a TaskSubmitted with actor_role = 'admin' for audit
- `terminate`: same as cancel-instance with admin actor + reason

Each requires the admin to provide a non-empty reason; UI enforces.

## 4. Reports — caching strategy

Aggregations over thousands of instances would be slow per-page-load. Cache at the application layer:

- Cache key: `{tenant_id}_{spec_code}_{period}` (e.g., `acme_LEAVE_30d`)
- TTL: 5 minutes
- Invalidation: on InstanceCompleted / InstanceCancelled events, mark the relevant cache key as stale

For SME scale (10s-1000s of instances per month), this is sufficient. If we hit perf walls: precomputed daily snapshots in a separate table (`SpecMetricsDaily`).

## 5. BPMN library choice

`bpmn-js` is the de facto open-source BPMN editor. Used by Camunda, Zeebe, etc. License: MIT. Already partially integrated in `bpm-ui/src/components/BpmnEditor.tsx`.

For properties panel: `bpmn-js-properties-panel` provides the standard right-side editor for node attributes. Optional but speeds up shipping; otherwise we hand-roll the panel using existing form primitives.

For initial version: hand-rolled properties panel embedding StepForms / StepApprovers / etc. — keeps consistency with wizard.

## 6. Live update without WebSocket

LiveCasesList polls every 30s for the current page's data. CaseDetail polls every 15s when open. SignalR / WebSocket is overkill for SME (a few cases active at a time); add later if a customer asks.

Polling pause when tab hidden (same pattern as notification polling).

## 7. Open questions

- **Multi-author conflict on designer**: two admins editing the same spec at once. v1: optimistic concurrency (PUT carries version; 409 on stale); UI shows conflict + reload. Real-time co-edit deferred.
- **Spec validation feedback**: when admin tweaks an expression in the designer, immediate CEL validation (re-uses validate-expression endpoint from `add-cel-expressions`)
- **Granular flow_admin auth**: today flow_admin is per-spec-code. If a customer wants "this admin can only edit purchase, not leave", the role assignment system already supports it via scope_ref.
- **Customer's specs vs ours**: do we need a "system templates" concept where a generic spec can be cloned? Defer; for SME each customer has bespoke flows anyway.
