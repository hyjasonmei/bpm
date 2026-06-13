# chef-agent — autonomous chef worker

A cross-platform **.NET 10 console app** that turns this machine into a chef
worker. Every 5 minutes a scheduler wakes it; it does **one** poll sweep of
all enabled environments and exits (one-shot model — crashes self-heal on the
next tick; a global file lock blocks overlapping runs).

Per poll, for each enabled environment it:

1. `GET /api/chef/flows/tasks` — the grouped work queue.
2. Runs **merge checks** for every Approved-awaiting-merge flow (cheap; no LLM):
   opens a `gh` PR (or, with no remote, posts a "merge manually" memo + TG
   reminder on a 24h cooldown), then detects merge (`gh ... mergedAt`, or branch
   ancestry when remote-less) and stamps `MergedAt` so admin-ui can Publish.
3. Picks **one** cook task — priority: retryable-stalled > user-answered hold >
   fresh submission — claims it (`Submitted→Cooking`) and runs a headless
   `claude -p` session inside a dedicated git worktree.
4. Classifies the outcome from the flow's state (Committed / OnHold / Incomplete
   / FlowGone) and notifies Telegram. One cook per poll keeps EF migrations
   serial and honours the single-session rule.

## Build

```bash
cd chef/agent
dotnet test                                   # unit tests
dotnet publish Bpm.ChefAgent -c Release       # produces bin/Release/net10.0/Bpm.ChefAgent.dll
```

## Configure

```bash
cp chef-agent.example.json chef-agent.json    # gitignored — holds tokens
```

Fill in:
- `environments[].chefToken` — local default is `dev-chef-token`; for azure-poc:
  `az keyvault secret show --vault-name kv-poc-flowcook -n chef-token -o tsv --query value`
- `telegram` — bot token + chat id (reuse your existing bot or make a new one)
- paths — `repoPath` (the bpm repo), `worktreeRoot` (where cook worktrees/logs/state live)

Keep `azure-poc` `enabled: false` until the local flow has been proven; the POC
stack is normally stopped, and the agent backs off unreachable environments.

## Run one poll by hand (recommended before scheduling)

```bash
dotnet run --project Bpm.ChefAgent -- chef-agent.json
```

⚠️ If a Submitted/OnHold flow exists, this starts a **real** `claude` cook.

## Prerequisites

- `dotnet` 10, `git`, `claude` CLI, and `gh` (for PR mode — `brew install gh && gh auth login`)
- `az` CLI logged in (only to read the azure-poc chef token)
- This machine's Claude cannot `ssh` to GitHub, so `gh` must auth over **HTTPS** (token)

## Schedule

### macOS (launchd)

```bash
# edit the dotnet/dll/config paths inside the plist first
cp com.flowcook.chef-agent.plist ~/Library/LaunchAgents/
mkdir -p ~/claude/bpm-cooks/logs
launchctl load ~/Library/LaunchAgents/com.flowcook.chef-agent.plist
# unload: launchctl unload ~/Library/LaunchAgents/com.flowcook.chef-agent.plist
```

### Windows (Task Scheduler)

```bat
schtasks /Create /SC MINUTE /MO 5 /TN flowcook-chef-agent ^
  /TR "dotnet C:\path\to\Bpm.ChefAgent.dll C:\path\to\chef-agent.json"
```

Windows prerequisites: Git for Windows (runs the bundled bash the chef skill
uses), `git config --global core.longpaths true` **and** the system
LongPathsEnabled policy (worktrees + node_modules blow past 260 chars), and
`gh` installed. Enable "Wake the computer to run this task" in the task's
Conditions, or pair with a power plan that doesn't sleep.

## Keep-awake (critical)

A sleeping machine doesn't run the schedule.
- macOS: `sudo pmset -a sleep 0` (or the Amphetamine app).
- Windows: a power plan that never sleeps, plus the task's wake setting above.

## Logs & state

- Logs: `<worktreeRoot>/logs/agent.log` + `agent.err.log`
- State: `<worktreeRoot>/agent-state.json` — retry counts, env-failure streaks,
  cooldown timestamps. Safe to delete (resets counters); the agent tolerates a
  corrupt file by starting fresh.

## Notifications (the only things that ping you)

cook started · committed · on-hold (your reply needed) · second failure (needs a
human) · PR opened · merge detected (ready to Publish) · no-remote merge
reminder (24h cooldown) · environment unreachable (after 3 misses, hourly).
