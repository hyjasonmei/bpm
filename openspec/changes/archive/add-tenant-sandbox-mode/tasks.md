# Tasks

## 1. Domain — Tenant flag (or new Tenant entity if missing)

- [ ] 1.1 If no `Tenant` entity exists yet: create `bpm-svc/src/Domain/Entities/Org/Tenant.cs` with `Id`, `Code`, `Name`, `SandboxMode` (bool), `SandboxConfigJson` (text, nullable). Otherwise extend existing.
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Sandbox/SandboxRedirect.cs` (Id, TenantId, Channel enum, OriginalTargetsJson, RedirectedTargetsJson, SampleSubject, DispatchedAt, Action enum {Redirected, Dropped})
- [ ] 1.3 Create `Channel` enum (Email, Webhook, Sms)
- [ ] 1.4 Create `SandboxAction` enum (Redirected, Dropped)

## 2. Persistence

- [ ] 2.1 EF configurations for Tenant + SandboxRedirect
- [ ] 2.2 Indexes on SandboxRedirect: (TenantId, DispatchedAt DESC), (TenantId, Channel, DispatchedAt DESC)
- [ ] 2.3 DbSets in AppDbContext
- [ ] 2.4 Migration `AddSandboxMode`
- [ ] 2.5 Apply locally; verify schema

## 3. Application — IOutboundGate

- [ ] 3.1 `Application/Sandbox/IOutboundGate.cs` interface — methods: `Apply(EmailMessage)`, `Apply(WebhookDelivery)`, `Apply(SmsMessage)` (each returns gated message + audit signal)
- [ ] 3.2 DTOs: `EmailMessage`, `WebhookDelivery`, `SmsMessage` (subset relevant to gating)
- [ ] 3.3 `OutboundGate` impl in Persistence layer, depends on AppDbContext + IClock
- [ ] 3.4 Email rewrite: clear To/Cc/Bcc, set To = sandbox.emailRecipients, prepend banner to BodyHtml + BodyText
- [ ] 3.5 Webhook rewrite: change Url to sandbox.webhookUrl, add header `X-BPM-Sandbox-Original-Url`
- [ ] 3.6 SMS rewrite: change To to sandbox.smsRecipients, prepend `[SANDBOX → originally to: <orig>]`
- [ ] 3.7 Empty config handling: if recipient list empty for that channel, mark Dropped + return null (caller skips send)
- [ ] 3.8 Each Apply call writes one SandboxRedirect row in same transaction (or via a fire-and-forget queue)
- [ ] 3.9 Register in DI

## 4. Email banner template

- [ ] 4.1 HTML template `<div style="border:2px solid #f59e0b; background:#fef3c7; padding:12px; margin-bottom:16px; font-family:monospace;">[SANDBOX MODE] Original recipients: ...</div>`
- [ ] 4.2 Plaintext template `[SANDBOX MODE] Original recipients: ...\n----\n`

## 5. API

- [ ] 5.1 `Api/Sandbox/SandboxController.cs` (NEW; admin-only)
- [ ] 5.2 `GET /api/sandbox/status` → returns current SandboxMode flag + SandboxConfig + last-toggled-at + last-toggled-by
- [ ] 5.3 `PUT /api/sandbox/status` body: `{ enabled: bool, config: { emailRecipients[], webhookUrl, smsRecipients[] } }` → toggle + persist config
- [ ] 5.4 `GET /api/sandbox/redirects?days=30&channel?` → returns recent SandboxRedirect rows
- [ ] 5.5 Authorization handler requiring `admin` role
- [ ] 5.6 Audit toggle action (separate audit row in existing audit table or extend SandboxRedirect with type=ToggleEvent)

## 6. Frontend

- [ ] 6.1 `bpm-ui/src/types/sandbox.ts`
- [ ] 6.2 `bpm-ui/src/lib/api/sandbox.ts`
- [ ] 6.3 `bpm-ui/src/screens/SiteSettings.tsx` (NEW; admin-only)
  - Toggle on/off (with confirm dialog)
  - Editor for emailRecipients (chip input), webhookUrl (url input), smsRecipients (chip input)
  - Recent redirects table (last 30 days, filterable by channel)
- [ ] 6.4 Wire SiteSettings into nav (admin persona only) — interim location; will move to bpm-admin-ui
- [ ] 6.5 `bpm-ui/src/components/SandboxBanner.tsx` (NEW) — top of every page when sandbox is on
- [ ] 6.6 Hook AppLayout to load tenant.sandboxMode on mount; render banner conditionally

## 7. Tests

- [ ] 7.1 Unit: gate.Apply(email) when sandbox OFF → identical message
- [ ] 7.2 Unit: gate.Apply(email) when sandbox ON with recipients → To rewritten, banner prepended, original recipients in audit
- [ ] 7.3 Unit: gate.Apply(email) when sandbox ON with empty recipients → returns Dropped marker, audit row written
- [ ] 7.4 Unit: gate.Apply(webhook) when sandbox ON → Url rewritten, X-BPM-Sandbox-Original-Url header set
- [ ] 7.5 Integration: toggle sandbox via API → next email through gate gets rewritten
- [ ] 7.6 Integration: redirects endpoint returns rows in DESC order

## 8. Documentation

- [ ] 8.1 Note in `add-notification-engine/tasks.md`: every dispatcher MUST call `IOutboundGate.Apply` before sending
- [ ] 8.2 Update CLAUDE.md if it has a feature list
