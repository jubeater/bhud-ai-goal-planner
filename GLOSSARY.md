# AI Goal Planner

A Blish HUD module in which an AI sets up and configures the player's other Blish HUD modules to reach a goal they describe in chat.

## Language

### People and conversation

**Player**:
The Guild Wars 2 player using the module.
_Avoid_: User, customer

**Chat**:
One conversation between the Player and the AI, from "New chat" until the next one.
_Avoid_: Session, thread, conversation

**Request**:
A message the Player sends in a Chat, describing what they want.
_Avoid_: Prompt, query, command

**Preset**:
A ready-made Request for a common goal, such as hero points, mastery points or XP, that the Player can edit before sending.
_Avoid_: Mode, template, profile

### Plans

**Plan**:
The AI's answer to a Request: a reply to the Player plus zero or more Actions.
_Avoid_: Suggestion, recommendation, config

**Action**:
One item in a Plan: install, enable or disable a Module, change a Setting, perform an Adapter operation, or give Advice.
_Avoid_: Step, change, task

**Advice**:
An Action that only informs the Player and that the module cannot carry out itself.
_Avoid_: Tip, hint

**Gap**:
The missing ability, which no Installed Module provides, that justifies an install Action.
_Avoid_: Reason, need

**Side Effect**:
An Action the Player did not ask for but which the Request requires, such as disabling a conflicting Module.
_Avoid_: Extra change, collateral

**Review**:
The Player's inspection of a Plan's Actions, choosing which to approve.
_Avoid_: Confirmation, preview

**Apply**:
Carrying out the approved Actions of a Plan.
_Avoid_: Execute, run, commit

**Auto-apply**:
The Player's opt-in choice to Apply Setting changes without Review. Installs, Marker Pack downloads and Side Effects are never auto-applied.
_Avoid_: Auto mode, trust mode

**Continuation**:
The AI's follow-up within the same Chat after an Apply, working from the new state (e.g. configuring a Module it just installed).
_Avoid_: Follow-up, second pass

**Undo**:
Restoring everything an Apply changed to its previous state.
_Avoid_: Revert, rollback

### Modules and configuration

**Module**:
A Blish HUD module (`.bhm`) other than this one.
_Avoid_: Addon, plugin, extension

**Installed Module**:
A Module present in the Player's Blish HUD, enabled or disabled.

**Catalog**:
The official Blish HUD module repository from which Modules can be installed.
_Avoid_: Store, marketplace, repo list

**Setting**:
A configuration value that a Module exposes through Blish HUD's own settings system, with its label, type and allowed range.
_Avoid_: Option, preference, config key

**Adapter**:
This module's built-in knowledge of one Module's configuration that lives outside its Settings, such as Pathing's Marker Category visibility.
_Avoid_: Plugin, integration, connector

**Marker Pack**:
A downloadable set of map markers and trails, shown in game by the Pathing Module.
_Avoid_: TacO pack, pack

**Marker Category**:
A named, nested group of markers within a Marker Pack that the Player can show or hide.
_Avoid_: Layer, filter

### Game and AI

**Owned Content**:
The expansions and other content the Player's GW2 account has access to.
_Avoid_: Unlocked content, DLC

**Provider**:
The LLM service the Player connects the AI to, identified by an endpoint, an API key and a model, such as DeepSeek, Ollama, OpenAI or Claude.
_Avoid_: Backend, vendor, model host

**Lookup**:
A read-only check the AI makes while planning, such as listing Installed Modules or reading the Player's progress.
_Avoid_: Query, tool call, fetch

**Token Budget**:
The per-Chat soft limit on tokens used, after which the AI asks before continuing.
_Avoid_: Cost limit, quota
