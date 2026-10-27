---
status: accepted
---

# Apply and Undo: one change per Module target, ten Applies kept, and a Continuation after every Apply

The first change this module makes in game (#6) needed three decisions that were not already in
ADR-0003 or ADR-0005: what a Plan may propose about one Module, what Undo remembers, and what the AI
sees after an Apply.

## Rules

- **A Plan changes each thing once.** `enable_module` and `disable_module` may each name a Module
  once per Plan, and `set_setting` may change a Setting once per Plan; the second Action on the same
  Module state or the same key is rejected with a reason. Two different Settings of one Module are
  fine, because "turn off X and Y in Mounts" is an ordinary Request. Two Actions that disagree about
  the same thing are not, because the Player cannot tell from the Review which one wins.
- **A disabled Module is enabled on its own.** Its Settings cannot be read until it is running, so
  the Validator refuses a Setting change on a disabled Module. The AI enables it, the Player applies
  that, and the Continuation configures it. This is the same shape as installing a Module and then
  setting it up.
- **Undo keeps the last ten Applies, and only real ones.** An Apply that changed nothing is not
  recorded. Each record holds what to write back — the previous value of a Setting, the previous
  state of a Module — and nothing else. Undo works through the records newest first and takes each
  record out as it goes, so a failed Undo cannot be retried into a growing list.
- **Undo is the module's, not the Chat's.** The journal belongs to the module, so New chat does not
  take it away: the Player's configuration is not part of the conversation.
- **The AI is told what an Apply did, and nothing about an Undo.** After an Apply the model gets one
  more turn of the same Request: a message saying what was applied and what failed, and then its own
  reply, which is the Continuation. Undo records and earlier Chats are never sent to a Provider; the
  model reads the restored state with a Lookup if it needs it.
- **Writes go where reads go.** Apply and Undo use the same `IBlishHudWorld` decorator the Lookups
  use, so every write happens on the game thread (ADR-0009), for the same reason: enabling a Module
  and writing a Setting runs that Module's own code.

## Considered Options

- **One Action per Module, period.** Simpler rule, but it would refuse the ordinary Request that sets
  two Settings of one Module, which is most of what this ticket is for.
- **Let the last Action on a Module win.** No rejection to explain, but the Player would be shown two
  rows for one thing and could not tell which applied.
- **Undo per Chat.** Matches "the plan I just approved", but loses the ability to undo after a New
  chat, which is when a Player most wants it.
- **Run the Continuation on the next Request instead of right after the Apply.** Fewer Provider round
  trips, but the AI would have to be reminded of state it could have read, and the Player would not
  see the module's follow-up until they typed something.

## Consequences

- One Apply is up to three model turns (deliver, auto-apply, Continuation), and each continuation
  costs tokens. The limit of three continuations per Request stops a model that answers every Apply
  with another Plan.
- A rejected Action is shown, not dropped: the Player has to be able to see what the AI wanted and
  why it cannot be done, and the AI is told how many of its Actions were refused.
- A model that describes changes in plain text instead of calling `submit_plan` costs the Player
  nothing but a dead end: the words are shown as an answer with no Actions, which is an Advice. The
  prompt asks for `submit_plan`, and it is never forced, because a Provider in thinking mode rejects
  a forced tool choice (issue #6, third comment).
- The Undo journal cannot put back a change made outside this module, and it does not try.
