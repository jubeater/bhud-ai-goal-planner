# Reviewing a PR

You're done when the report names the commit it reviewed, every finding is reproduced or marked as a judgement call, and the PR's in-game check is done or handed to the maintainer.

## Code review

1. **Check out the PR head** in a detached worktree of your own: `.scratch/reviews/pr-<n>` (`git worktree add --detach .scratch/reviews/pr-<n> <commit>`). Remove it with `git worktree remove` when the review is done.
2. **Run the `code-review` skill** with `origin/main` as the fixed point. The spec is the ticket the PR names. Earlier reviews of the PR are in its comments.
3. **Reproduce before you rate.** For every behaviour finding, write a temporary test in your review worktree that fails on the PR's code. Rate it P1 only when that test shows wrong behaviour the Player would hit. Keep these tests out of the PR.
   - **P1:** wrong or unsafe behaviour (data loss, a leak, a broken safety rule such as Review or Undo).
   - **P2:** a spec criterion not met, or a repo standard broken.
   - **P3:** a judgement call (a code smell, docs drift).
   - When an earlier finding turns out to be wrong, say so and withdraw it.
4. **Write the report and post it as a comment on the PR** (`gh pr comment <n> --body-file <file>`), so the implementing agent and the orchestrator can link to it:
   - first line: the PR and the exact commit reviewed
   - then `## Standards` and `## Spec`, each finding with a file and line link and a concrete failing input
   - then the test count, the Stj5Check result, and whether anything was changed in the repo (nothing should be)
   - last line: findings per axis and the worst one in each

## Scoped review

When the orchestrator asks for a **scoped review**, run the code review on two things only:

- each finding the request links (from the previous review or in-game check): fixed in `<previous commit>..<commit>`, or still open
- regressions in the code that range changes

Code review steps 1, 3 and 4 apply unchanged. List anything else you notice under `## Out of scope, not findings`, without a P level.

## In-game check

Needed when the PR changes what the Player sees, or reads or writes Blish HUD or Guild Wars 2 data. Run it on the PR's latest commit, once the code review has no open P1 or P2 findings.

1. **Launch:** `scripts/dev-run.ps1 <worktree>` stops Blish HUD, builds that worktree and launches Blish HUD with its module. The maintainer keeps the game open on a character. Send no input to the game itself.
2. **Check** each acceptance criterion the PR lists as in-game only. Send Requests and Chat operations with `scripts/dev-ask.ps1` and `dev-op.ps1`, and capture the screen with `dev-shot.ps1`. Read the Blish HUD log (`Documents\Guild Wars 2\addons\blishhud\logs`) for the module's Lookup and round-trip lines, and for warnings or errors.
   - The maintainer makes every click: Blish HUD settings, hover text, and anything in the game. Ask for the change or the screenshot you need, then continue. A script answering `channel_off` means the developer channel setting is off.
   - Hand comparisons with in-game panels (Hero, Mastery) and anything that needs a particular map to the maintainer, with what to compare.
3. **Comment on the PR:** the commit checked, each criterion with pass or fail and what was seen, and anything handed to the maintainer. The repo is public: write "matched what the maintainer gave" instead of character or account names, and leave out local paths and screenshots.
4. **Stop Blish HUD** when done (`scripts/dev-run.ps1 -Stop`), so the worktree can be removed.
