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
4. **Write the report:**
   - first line: the PR and the exact commit reviewed
   - then `## Standards` and `## Spec`, each finding with a file and line link and a concrete failing input
   - then the test count, the Stj5Check result, and whether anything was changed in the repo (nothing should be)
   - last line: findings per axis and the worst one in each

## In-game check

Needed when the PR changes what the Player sees, or reads or writes Blish HUD or Guild Wars 2 data. Run it on the PR's latest commit, once the code review has no open P1 or P2 findings.

1. **Launch:** `scripts/dev-run.ps1 <worktree>` stops Blish HUD, builds that worktree and launches Blish HUD with its module. The maintainer keeps the game open on a character. Send no input to the game itself.
2. **Check** each acceptance criterion the PR lists as in-game only. Read the Blish HUD log (`Documents\Guild Wars 2\addons\blishhud\logs`) for the module's Lookup and round-trip lines, and for warnings or errors.
   - Hand comparisons with in-game panels (Hero, Mastery) and anything that needs a particular map to the maintainer, with what to compare.
3. **Comment on the PR:** the commit checked, each criterion with pass or fail and what was seen, and anything handed to the maintainer.
4. **Stop Blish HUD** when done (`scripts/dev-run.ps1 -Stop`), so the worktree can be removed.
