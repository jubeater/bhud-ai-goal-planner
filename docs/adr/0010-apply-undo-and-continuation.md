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
- **Undo keeps the last ten Applies, and only real ones.** A write that asks for the value or the
  state already in place is not a change: nothing is written, no Undo step is recorded, and the Chat
  says it was already so. An Apply whose every Action was already so is not recorded at all, so a run
  of no-ops cannot push a real change out of the journal. Each record holds what to write back — the
  previous value of a Setting, the previous state of a Module — and nothing else. Undo works through
  the records newest first and takes each record out as it goes, so a failed Undo cannot be retried
  into a growing list.
- **An Apply the Player stops part-way keeps what it did.** A cancellation does not roll back or
  discard: the Actions that finished stay applied, are recorded in the journal and are reported, and
  the Request ends as cancelled. There is no Continuation, because the Continuation belongs to an
  Apply that finished.
- **Undo is the module's, not the Chat's.** The journal belongs to the module, so New chat does not
  take it away: the Player's configuration is not part of the conversation.
- **The AI is told what an Apply did, and nothing about an Undo.** After an Apply the model gets one
  more turn of the same Request: a message saying what was applied and what failed, and then its own
  reply, which is the Continuation. Undo records and earlier Chats are never sent to a Provider; the
  model reads the restored state with a Lookup if it needs it.
- **A Continuation never takes a Review away.** When Auto-apply has applied part of a Plan and the
  rest is still waiting for the Player — including a Review where nothing can be applied any more —
  the module does not ask the model again: its next `submit_plan` would replace the Review the Player
  is looking at. The module reports what it applied and stops.
- **What Auto-apply did belongs to the Review it came with.** That outcome is given to the model
  exactly once, when that Review is resolved, and cleared then. A Review is resolved when the Player
  applies it — with Actions ticked or with none, since choosing nothing is still a decision — when the
  Player dismisses it, or when a new Plan replaces it. Resolving it is what starts the Continuation,
  and the message carries everything applied in that Request, so the model sees the state it made
  either way. A new Request that leaves the Review open does not resolve it: asking a question is not
  a decision about the Plan. A dismiss does not spend a turn of its own: the outcome travels in the
  history and the next Request reads it.
- **An Action that does not say it is not a Side Effect is one.** `is_side_effect` is required by the
  schema, and only a real `false` — or the text "false" — is read as "the Player asked for this".
  A missing or malformed flag makes the Action a Side Effect: it still shows in the Review, but
  Auto-apply leaves it alone, so a model that forgets the flag cannot cause an unreviewed change
  (ADR-0005).
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
  with another Plan. A Plan that leaves a Review open ends the turn there, so the Continuation the
  model would have written arrives with the Player's decision instead of before it.
- A Plan the Player never decides on still leaves what Auto-apply did in place, and the Chat has no
  Continuation to show for it until the Player applies or dismisses the rest. That is the price of not
  taking the Review away from them. Asking another question in the meantime keeps the Review, and the
  outcome that belongs to it, waiting.
- A rejected Action is shown, not dropped: the Player has to be able to see what the AI wanted and
  why it cannot be done, and the AI is told how many of its Actions were refused.
- A model that describes changes in plain text instead of calling `submit_plan` costs the Player
  nothing but a dead end: the words are shown as an answer with no Actions, which is an Advice. The
  prompt asks for `submit_plan`, and it is never forced, because a Provider in thinking mode rejects
  a forced tool choice (issue #6, third comment).
- A model that never sends `is_side_effect` gets nothing auto-applied: every Action it proposes waits
  for Review. That is slower for the Player and safer for their configuration, and the prompt says to
  always send the flag.
- The Undo journal cannot put back a change made outside this module, and it does not try.
