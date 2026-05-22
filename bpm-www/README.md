# bpm-www — flowcook marketing site

The public-facing product site. Customer-facing copy, demos, pricing,
sign-up flow that funnels to admin onboarding. Lives separately from
the four-service stack (`bpm-svc` / `bpm-admin-svc` / `bpm-ui` /
`bpm-admin-ui`) because it has zero shared runtime concerns.

## Status — empty skeleton

The folder is intentionally bare. Before scaffolding, three decisions
to make:

### 1. Tech stack

| Option | Pros | Cons |
|---|---|---|
| **Astro** | Best-in-class for marketing — static-first, MDX content, partial hydration, fast lighthouse scores | New tech in the repo (other apps are Vite+React) |
| **Next.js (App Router)** | SSR / ISR / SEO baked in, easy Vercel deploy, big ecosystem | Heavier than needed for static marketing; different from Vite stack |
| **Vite + React + Tailwind** | Matches `bpm-admin-ui` / `bpm-ui` exactly — zero new tooling | Marketing pages over-engineered; SEO needs SSG plugin |
| **Plain HTML + Tailwind** | Simplest possible; deploys anywhere | Hard to scale beyond 5 pages; no MDX for content authoring |

Default recommendation: **Astro** — designed for this exact use case.
Going Vite-React for stack consistency is also fine.

### 2. Hosting

- **Vercel / Netlify** — turnkey, free for marketing tier
- **Cloudflare Pages** — same shape, CDN-included
- **Self-host alongside bpm-admin-svc** — if customer demos need a
  shared origin / cookies

### 3. Content shape (rough page list)

- `/` — hero + value prop + 3-step demo loop visual
- `/features` — AI Kitchen / chef / sandbox
- `/pricing` — TBD model from CLAUDE.md (年費 + 顧問點數)
- `/customers` — case studies (partner-brought flows as the proof)
- `/demo` — embedded video or interactive walkthrough
- `/contact` / `/signup` — funnel to admin

## How to scaffold (once decided)

If **Astro**:
```bash
cd ~/claude/bpm
npm create astro@latest bpm-www -- --template basics --typescript strict --no-install --no-git --yes
cd bpm-www && npm install
# then add Tailwind: npx astro add tailwind
```

If **Vite + React**:
```bash
cd ~/claude/bpm
npm create vite@latest bpm-www -- --template react-ts
cd bpm-www && npm install
# then Tailwind: per https://tailwindcss.com/docs/guides/vite
```

Either way, follow the existing repo pattern: a `CLAUDE.md` in the
folder documenting conventions, `tsconfig.app.json` for tsc, no test
framework wired (rely on tsc + manual boot).

## References

- `/Users/jason/claude/bpm/CLAUDE.md` — overall product positioning
  (商業 BPM 平台, 中小企業, AI onboarding + 無痛上線驗收)
- `bpm-admin-ui/CLAUDE.md` — closest neighbour for stack conventions
  if going Vite+React
