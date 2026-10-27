# Orchestrating a ticket

You coordinate the other agents and don't write code. You're done with a ticket when its PR has a clean review of its latest commit and a passed in-game check, both as PR comments, and you've told the maintainer it's ready to merge. Or when you've stopped and asked the maintainer.

Delegate with the `codexhost-delegation` skill: an independent session per task, with a short task name (`impl #32`, `review #32 r2`). Watch each session rather than polling, and read it before judging its work.

## Steps

1. **Implement:** delegate to an implementing agent (OpenCode, AGY):
   > `/implement #<n>` — read AGENTS.md and docs/agents/implementing.md first. *(When another ticket runs in parallel:)* Another agent is doing #<m> at the same time.

   Watch it. When it stops, read it, and check that a PR exists for the ticket with a pushed commit.
2. **Review:** delegate to Codex:
   > Review PR #<pr> at commit `<sha>` against `origin/main` — read AGENTS.md and docs/agents/reviewing.md first. Post the review as a PR comment.

   Watch it, then read the review comment.
3. **Judge each finding** against the ticket, the ADRs and the code. Reviewers overstate, and fix instructions can be wrong. For each one, decide:
   - **accept:** it's real and in scope
   - **reject:** say why, citing the ticket line, the ADR or the code
   - **ask the maintainer:** see [When to stop and ask](#when-to-stop-and-ask)
4. **Fix round:** send the implementing session a follow-up that links the review comment and lists the accepted findings by number, with your reason for each rejected one. Then go back to step 2 with the new commit. Never paste the review text.
5. **In game:** when the latest review has no accepted P1 or P2 findings and the PR touches what the Player sees, or Blish HUD or GW2 data, delegate the in-game check to Codex (docs/agents/reviewing.md, "In-game check"). Read its PR comment.
6. **Report to the maintainer:**
   - the PR link and its latest commit
   - the number of review rounds
   - what the in-game check covered, and what it handed to the maintainer
   - anything you rejected or left open

   The maintainer merges.

## When to stop and ask

Stop delegating and ask the maintainer when:

- a finding is P1, or you'd accept a finding the ticket doesn't cover (a scope change)
- the ticket, an ADR and the code disagree, or a decision isn't written down anywhere
- a PR reaches its 4th review round
- an agent is stuck on an approval or permission prompt, or asks for something outside its manual
- the in-game check needs a comparison with an in-game panel (Hero, Mastery) or a particular map
- it's time to merge

## Rules

- **Links, not text.** Hand work over by linking PRs, comments and commits, so the record lives on GitHub and your context stays small.
- **One commit per review.** Each review names the commit it reviewed. A new commit gets a new review.
- **Parallel tickets:** run at most two at once, on tickets whose shared files barely overlap. Merge one first, then tell the other implementing session to update from `main` (merge, not rebase) and request a new review.
- **No input to the game.** In-game actions go through the developer channel (#32). Computer use is for screenshots only, to check layout. Clicks are only on Blish HUD's own windows, only when the maintainer has allowed them, and never keyboard input.
- **Your own changes:** if the process itself needs to change (a manual, `AGENTS.md`), propose it to the maintainer instead of editing it mid-ticket.
