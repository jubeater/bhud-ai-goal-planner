# AGENTS.md

## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues for jubeater/bhud-ai-goal-planner, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Your role

- **Implementing a ticket:** read `docs/agents/implementing.md` before you start.
- **Reviewing a PR, or checking one in game:** read `docs/agents/reviewing.md` before you start.
- **Orchestrating other agents through a ticket:** read `docs/agents/orchestrating.md` before you start.

Either way:

- **Reading:** stay in `src/`, `tests/` and `docs/`. For a Blish HUD API, search `.scratch/blish-hud-src` for that one class name.
- **Experiments:** try things out as test cases in `tests/`, so nothing gets built into `%TEMP%`.
- **Test output:** run `dotnet test --nologo -v q` and show only failing lines.
- **Stj5Check:** build `tests/AiGoalPlanner.Stj5Check` and run its `.exe`. It must print `RESULT: OK` (ADR-0008).
- **In game:** in-game checks run through the developer channel: `scripts/dev-ask.ps1`, `dev-op.ps1` and `dev-shot.ps1` (README, "Scripted in-game checks"). They need Blish HUD started by `scripts/dev-run.ps1` and the module's developer channel setting on.
- **Merging:** the maintainer squash-merges a PR once its latest review has no open findings and its in-game check has passed.
