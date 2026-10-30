# Implementing a ticket

You're done when the ticket's PR is open, every acceptance criterion the code can meet has a test, the full suite and the Stj5Check pass, and the PR says which criteria still need an in-game check.

## Steps

1. **Worktree:** work in a git worktree on a new branch from `main`. If your session started in one, use it; otherwise create one next to the repo: `../AI_assistant-worktrees/<branch>`. Work only there. The main `AI_assistant` folder stays on `main`.
2. **Implement:** run the `implement` skill on the ticket (`/implement #<n>`). It works test-first with `tdd`, reviews your work with `code-review`, and commits.
3. **Push and open the PR:** write the body with the `pr` skill, name the ticket (`Closes #<n>`), and list the acceptance criteria that only an in-game check can confirm.
4. **Review rounds:** for each review finding, write a failing test first, then fix it. Push each round as a new commit, so the reviewer can compare against the commit they reviewed.

## Rules

- **External APIs first:** when a ticket reads data the code doesn't use yet (a GW2 API endpoint, another module's files), start by looking at real responses. Comment on the ticket with what they show (indexing, missing fields, error codes), then build the tests on that data.
  - API responses: save them under `tests/AiGoalPlanner.Tests/Fixtures/`.
  - Content owned by others, such as marker packs: write small hand-made files in the same shape instead.
- **Parallel agents:** another agent may be on a different ticket at the same time. Put new behaviour in new files, and only append to the shared files, keeping their existing names and order: `IBlishHudWorld`, `BlishHudWorld`, `GameThreadWorld`, `Fakes/FakeWorld`, `Lookups.cs`, `ToolRegistry.cs`, `SystemPrompt.cs`, `Chat.cs`, `ChatTests.cs`.
- **Game thread:** read and change Blish HUD state on the game thread through `GameThreadWorld`. Capture anything needed after an `await` before it (ADR-0009).
- **Logging:** log types, counts and durations only (README, "Diagnosing a Request"). `LogSafetyTests` enforces this for `Core/`, `Providers/` and `World/`.
- **Files:** end every file with exactly one newline (`.editorconfig`).
