# bpm-admin-ui

Admin / customer-config SPA — React 18 + Vite + Tailwind v4 + shadcn
+ bpmn-js + lucide-react.

## Layout

`src/flowcook/` is the only shell. Legacy `src/screens/` admin shell
was purged in `6b26f75`; `src/screens/onboarding/` survives because
the flowcook shell still hosts the 9-step AI Kitchen wizard from
there.

```
src/flowcook/
  Root.tsx          ← AppShell + auth gate + routing
  app/              ← layout primitives (sidebar / topbar / page hint)
  auth/             ← login + session
  pages/
    AiKitchenPage.tsx   ← wraps screens/onboarding wizard
    UserRolePage.tsx    ← principal / role / delegation / dept mgmt
    userrole/           ← per-section editors
  api/              ← typed clients against bpm-admin-svc
  api.ts            ← fetch wrapper
  types.ts
src/screens/onboarding/  ← CoPilotCanvas + Onboarding shell + step components
```

Five-page nav was the plan; **AI Kitchen + User & Role** are the two
that have landed so far. Sandbox / Audit / Site Setting pages are
open work.

## Backend pairing

`bpm-admin-ui` talks only to `bpm-admin-svc` (port 5266) — same
Clean-Architecture five layers as `bpm-svc` (Api / Application /
Domain / Persistence / SeedCli; csproj names `Bpm.Admin.*`). It does
**not** read or write the bpm-svc runtime DB directly — admin owns
the canonical identity tables; bpm-svc reads them via SharedX
DbSets. See `../bpm-svc/CLAUDE.md` § "SharedIdentity".

## Ownership

This tree is fully **lead** territory. Chef writes only inside the
per-flow Features folders on the bpm-svc / bpm-ui side; `bpm-admin-*`
is never chef's. See [`../lead/skill/SKILL.md`](../lead/skill/SKILL.md)
for the full path map.

## Type-check

`npx tsc -p tsconfig.app.json --noEmit`. No JS test runner — rely on
tsc + manual boot (`npm run dev`, port 5174) + chrome-devtools.

## Conventions

- Root [`../CLAUDE.md`](../CLAUDE.md) — product context, 5-project
  architecture, Clean Architecture five-layer convention for both
  backends
- [`../bpm-admin-svc/`](../bpm-admin-svc/) — backend csprojs
  (`Bpm.Admin.{Api, Application, Domain, Persistence, SeedCli}`)
- [`../lead/skill/SKILL.md`](../lead/skill/SKILL.md) — lead boundary
- [`../README.md`](../README.md) — run + seed admin DB + ports
