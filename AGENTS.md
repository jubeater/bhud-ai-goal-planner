# AGENTS.md

## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues for jubeater/bhud-ai-goal-planner, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## From ticket to merge

1. **Implement:** the agent that takes a ticket runs the `implement` skill on it (`/implement #<n>`). That skill works test-first with `tdd`, reviews its own work with `code-review`, and commits. Then push the branch and open a PR that names the ticket.
2. **Review:** a second agent runs the `code-review` skill on the PR, with `origin/main` as the fixed point, and starts its report with the commit it reviewed. Each round of fixes is a new commit and gets a new review of that commit.
3. **In game:** every PR that changes what the Player sees, or reads or writes Blish HUD or Guild Wars 2 data, is checked in game on its latest commit before merge. Launch it with `scripts/dev-run.ps1 <worktree>`. Comment on the PR with what was checked, on which commit, and the result. Comparisons with in-game panels (Hero, Mastery) need the maintainer.
4. **Merge:** the maintainer merges (squash) once the latest review has no open findings and the in-game check has passed.

## Working on a ticket

- **Worktree:** do each ticket in its own git worktree on a branch from `main`, next to the repo (`../AI_assistant-worktrees/<branch>`). The main `AI_assistant` folder stays on `main`.
- **Parallel agents:** another agent may be on a different ticket at the same time. Put new behaviour in new files, and only append to the shared files (`IBlishHudWorld`, `BlishHudWorld`, `GameThreadWorld`, `Fakes/FakeWorld`, `Lookups.cs`, `ToolRegistry.cs`, `SystemPrompt.cs`, `Chat.cs`, `ChatTests.cs`), keeping their existing names and order.
- **External APIs first:** when a ticket reads data from an API the code doesn't use yet (GW2 API, another module's files), start by fetching real responses, write what they show (indexing, missing fields, error codes) as a comment on the ticket, and save the responses under `tests/AiGoalPlanner.Tests/Fixtures/` as the test data.
- **Tests:** write the failing test first. Run `dotnet test --nologo -v q` and show only failing lines. Before pushing, also build `tests/AiGoalPlanner.Stj5Check` and run its `.exe`; it must print `RESULT: OK` (ADR-0008).
- **Experiments:** try things out as test cases in `tests/`, so they keep running and leave no executables in `%TEMP%`.
- **Reading:** stay in `src/`, `tests/` and `docs/`. For a Blish HUD API, search `.scratch/blish-hud-src` for that one class name.
- **Logging:** log types, counts and durations only (README, "Diagnosing a Request"). `LogSafetyTests` enforces this for `Core/`, `Providers/` and `World/`.
- **Files:** end every file with exactly one newline (`.editorconfig`).
- **Commits:** add a new commit for each round of fixes and push it normally, so reviewers can compare against the commit they reviewed.