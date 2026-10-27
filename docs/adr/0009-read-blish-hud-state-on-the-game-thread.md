---
status: accepted
---

# Read Blish HUD state on the game thread

The Lookups run while the model thinks, which is on a thread-pool thread, and `BlishHudWorld` read
module Settings from there. That read is not passive: `SettingEntry.Value` runs other modules'
compliance-requisite code and loads their lazy subcollections, and Event Table logged
`Max … resolution is invalid. X: 2 - Y: 2` after its Settings were read that way during the in-game
test of #5 (PR #19). Blish HUD and the modules expect their state to be read on the game thread, so
every read is now handed there first.

## Rules

- A Lookup never touches Blish HUD state directly. It goes through `IBlishHudWorld`, whose
  implementation in the module is `GameThreadWorld` wrapping `BlishHudWorld`.
- `GameThreadWorld` queues each read with `IGameThread` and awaits the result; `OverlayGameThread`
  is the adapter that queues it on Blish HUD's update loop (`OverlayService.QueueMainThreadUpdate`)
  and completes a `TaskCompletionSource`.
- The Chat stays off the game thread. Only the read runs there, and the wait happens where the Chat
  is, so a Lookup never blocks the update loop.
- Lookups read game state (Mumble Link, module settings) on the game thread first, then await
  GW2 API calls off it. HTTP and anything after an await must not touch Blish HUD state: every
  value needed after an await (such as character name, map ID, or CurrentMumbleMapName) is
  captured before any await.
- Cancelling a Request while a read waits its turn ends the wait; the queued call still runs later
  and finds nobody waiting, so it reads nothing.
- A new Lookup gets this for free: the wrapper is around the world, not around each tool.

## Considered Options

- **Marshal inside each Lookup tool.** Puts the thread rule in every tool, and #7 adds more of them;
  one that forgets is a bug that only shows up in game.
- **Read on a dedicated thread of the module's own.** Still not the game thread, and still runs other
  modules' code where they do not expect it.
- **Copy the state once per Request, on the game thread.** Fewer hops, but a Request would answer
  from a snapshot taken before the model started thinking, and a module enabled mid-Request would
  not be seen.

## Consequences

- A read waits for the next update loop, so a Lookup can be a frame behind; nothing the Player sees
  depends on it being instant.
- If the update loop stops turning, the read never runs and the Lookup waits until the Player
  cancels the Request. That is the same wait any other Blish HUD work has.
- The queued call runs whatever the Request's fate, so it must do nothing when the wait is already
  finished. It reads nothing in that case.
- `GameThreadWorld` is testable without Blish HUD, and the tests that hold a read open or cancel one
  drive the real wrapper.
