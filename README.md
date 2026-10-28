# AI Goal Planner

A [Blish HUD](https://blishhud.com/) module in which an AI sets up and configures your other Blish HUD
modules to reach a goal you describe in chat. The design is tracked as GitHub issues on this repo;
`GLOSSARY.md` and `docs/adr/` explain the terms and the decisions behind it.

**Status:** the Chat talks to a real Provider and answers from the Player's real setup: the AI can
list the installed modules and read one module's settings, and it proposes changes as a Plan through
`submit_plan`. The Review under the conversation shows each Action with the value it would change,
why, and a badge when it is a Side Effect; **Apply** changes what the Player approved and enables or
disables Modules, **Undo** puts the last ten Applies back, and Auto-apply (off by default) may take
Setting changes on its own. A corner icon opens the Chat window and **Provider settings** stores the
endpoint, the model and (optionally) the API key.

## Build

Needs the .NET SDK (8.0 or later) on Windows. No Visual Studio required.

```
dotnet build
dotnet test
```

`dotnet build` produces the module as `src/AiGoalPlanner/bin/<Configuration>/net48/AiGoalPlanner.bhm`
(Debug and Release). The `.bhm` is a zip of `AiGoalPlanner.dll` (with its PDB embedded), the
`manifest.json`, the module's own `ref/` content, and the libraries Blish HUD does not ship (see
[The Provider client and what the module ships](#the-provider-client-and-what-the-module-ships)).
Blish HUD's own assemblies are referenced from the `BlishHUD` NuGet package and are deleted again
before the zip, because Blish HUD already has them loaded; a `.bhm` an earlier build left in the
output folder is removed too, so one build never packs the previous zip into the new one. Blish HUD
holds that leftover open while the module is loaded, and then the build only warns: the stale zip
rides along as one inert extra entry rather than failing a build that has nothing wrong with it.

Building while Blish HUD is running can fail on a locked `.bhm`; send the zip elsewhere instead:
`dotnet build -p:BlishHUDModuleOutputDir=obj\bhm\`. The folder is created if it does not exist.

To try a worktree in game, `scripts\dev-run.ps1 <worktree>` stops every Blish HUD process (it
restarts itself when the game closes and keeps the loaded `.bhm` open), builds that worktree's module
into `obj\bhm\` and launches Blish HUD with `--debug --module` pointing at it. `-Stop` only stops
Blish HUD, for example before removing a worktree. Blish HUD is looked for in `BLISHHUD_DIR`, then in
`Desktop\Blish.HUD` under the user profile.

## Connecting a Provider

The Chat talks to any Provider that speaks the OpenAI-compatible **Chat Completions** API (ADR-0007),
so Ollama, DeepSeek, OpenAI and the rest work with only the endpoint, the model and the key changed:

1. Open the Chat window from the corner icon, then click **Provider settings**.
2. Pick a **Provider** from the suggestions (Ollama Cloud, local Ollama, LM Studio, DeepSeek, OpenAI,
   OpenRouter, Claude) or choose **Custom** to enter any endpoint URL. A base URL is enough;
   `/chat/completions` is appended.
3. Enter the **model name**: `deepseek-flash`, `beam:cloud`, `qwen3.5:9b`. The module remembers each
   Provider's model so you can switch between them easily.
4. Paste the **API key** if the Provider needs one: put the key on the clipboard and click
   **Paste key**. A local Ollama needs no key at all.
5. Click **Save**. The next Request uses these settings; on first use of a Provider and model, a quick
   tool-calling test runs. Models that cannot call tools are refused with a clear Notice.

The key never goes into Blish HUD's `settings.json`, and it is never logged. It is encrypted with
Windows DPAPI for the current Windows user and written to the module's data folder as one
`provider-<hash>.key` file per endpoint — the endpoint is hashed into the file name, so the URL is
not written next to it. **Clear key** deletes the file.

Blish HUD 1.3.0 has no masked text box, so a key is never typed into one: **Paste key** reads the
clipboard and stores the key directly. The view only ever shows what is stored as
`Key saved (...abcd)` — the last four characters — or `No key`, so a key is never drawn on screen,
never appears in a Notice and never reaches the log.

Both fields start empty, and what they show until the Player types is a hint rather than a value:
`e.g. https://your-provider.example/v1` and `Model name from your Provider`. Neither names a Provider,
because a placeholder that reads like one looks pre-filled.

Every Request carries one system message ahead of the Player's words, so a model does not introduce
itself as whatever product it was trained to be:

> You are AI Goal Planner, an assistant inside the Blish HUD module of the same name. You are running
> on model {model} via {endpoint host}. Don't claim to be ChatGPT or any other product.

It is built from the same Settings as the Provider client, so it names the model and the host the
next Request really uses, and it is not one of the Chat's messages: the Player never reads it, and New
chat leaves it in place. This is the identity message only — #5 grows it into the full system prompt.

## The Provider client and what the module ships

`OpenAiCompatibleChatClient` is the one Client for every Provider (ADR-0007): it speaks the OpenAI
Chat Completions API over `HttpClient` and reaches the Chat through `Microsoft.Extensions.AI`'s
`IChatClient`. Replies are not streamed; a reply arrives complete.

The AI libraries had to prove that they load inside Blish HUD's process, with raw HTTP behind the same
`IChatClient` as the fallback if they do not. The module takes the fallback for the wire format and
keeps `Microsoft.Extensions.AI` for the seam:

- **No Provider SDK.** The OpenAI SDK brings `System.ClientModel` and a second JSON stack into a
  .NET Framework process that already has `System.Text.Json` loaded through Gw2Sharp, and there are
  no binding redirects to sort out the versions.
- **Raw HTTP for the wire format.** The request body is built and the reply is read with
  `Newtonsoft.Json`, which Blish HUD already loads (13.0.1), so no extra JSON library is needed. The
  one place the module touches `System.Text.Json` is at the tool seam: `Microsoft.Extensions.AI`
  describes a tool's schema as a `JsonElement`, which the client copies out as text.
- **`Microsoft.Extensions.AI.Abstractions` is the one library Blish HUD does not have.** The `.bhm`
  therefore adds that assembly and its own dependency closure, and nothing else.

`CopyLocalLockFileAssemblies` is on so those libraries travel in the `.bhm`. That is the documented
way to ship a module dependency: Blish HUD loads an assembly packaged with the module when the module
references it. Blish HUD's own assemblies (`Gw2Sharp`, `MonoGame`, `SharpDX`, `Newtonsoft.Json`,
...) are deleted before the zip, so the `.bhm` carries nothing Blish HUD already has.

A process holds one version per assembly name
([blish-hud/Blish-HUD#740](https://github.com/blish-hud/Blish-HUD/issues/740)), and Blish HUD
embeds `System.Text.Json` 5.0 in `Blish HUD.exe` and loads it first. So the module always runs on
5.0, even though it ships 10.0, and any `Microsoft.Extensions.AI` path that needs a newer
`System.Text.Json` fails at runtime. [ADR-0008](docs/adr/0008-stay-on-blish-huds-system-text-json-5.md)
lists what that rules out (for example `AIFunctionFactory.Create`), and a CI test runs the Chat
against Blish HUD's own assemblies to catch it.

**In game:** enable the module in Blish HUD 1.3.0, open the Chat and send a Request. A library that
fails to load shows up in `Documents\Guild Wars 2\addons\blishhud\logs` rather than as a build error;
that failure mode is what the packaging above is there to prevent.

## Debug in VS Code

`.vscode/launch.json` starts Blish HUD with this module loaded, so breakpoints hit:

- Blish HUD is expected at `%USERPROFILE%\Desktop\Blish.HUD\Blish HUD.exe`; edit `program` and
  `cwd` in `.vscode/launch.json` if yours is elsewhere.
- The configuration builds the module first (`tasks.json`), then runs
  `Blish HUD.exe --debug --module <path to AiGoalPlanner.bhm>`. `--debug` makes Blish HUD rethrow
  module exceptions to the debugger.
- Logs are written to `Documents\Guild Wars 2\addons\blishhud\logs`.

## Diagnosing a Request

Every Provider round trip and every Lookup leaves one line in the Blish HUD log at debug level, so a
slow or odd Request can be read back afterwards:

```
Provider round trip 1: 812 ms, finish_reason=tool_calls, tool_calls=1
Lookup get_module_settings: 14 ms, module=bh.pathing, status=ok, settings=200 of 431, 18422 chars
```

The lines carry timing, finish reasons and counts — what a Lookup read and how big its answer was —
and never a Setting's value, the Player's words or anything a Provider sent back.

## Scripted in-game checks

The maintainer's by-hand in-game checks can run as scripts through the developer channel: request
files in `Documents\Guild Wars 2\addons\blishhud\aigoalplanner\dev\inbox` run against the real Chat
in a running Blish HUD — the same methods the window's buttons call — and answers land as
`dev\outbox\<id>.json`. Responses hold the Chat messages and Notices added, the open Review with
each Action's current → proposed value, reason, Side Effect badge and acceptance, every Lookup and
Provider round trip with the same details the debug log keeps, whether Undo is available, and
`ok` or a fixed error reason. The module stays advisory: nothing sends input to the game.

Three gates, all required: a Debug build (a release `.bhm` holds no channel at all), Blish HUD
started with `--debug` (`scripts\dev-run.ps1` does this), and the module setting *Developer channel
(for testing only)*, off by default. The setting explains what is still missing while the channel
is closed.

```
scripts\dev-ask.ps1 "Where am I?"                # send a Request, print the answer
scripts\dev-ask.ps1 "..." -NewChat               # start over, then ask
scripts\dev-op.ps1 apply 0,2                     # apply, cancel_review, undo, new_chat, state
scripts\dev-shot.ps1 [-Out path]                 # PNG of the GW2 window (screen if unfound)
```

`dev-ask` waits up to 120 s for the response and prints it readably. Requests go through the
Provider the Player set up and cost tokens like any Request.

## Known limitations

- **The AI's answers vary.** The same Request can get a different answer, or a different Plan, each
  time, and the module cannot make a model behave the same way twice. Smaller and local models
  sometimes skip a Lookup they should have made, or write their own tool-call syntax into the reply
  instead of calling the tool (`assistant to=get__location…`). The log shows this as a round trip
  with `finish_reason=stop, tool_calls=0`. Asking again usually works; a model with better tool
  support makes it rarer.
- **Hero challenge counts can be unknown.** The Guild Wars 2 API returns no hero-point data for some
  characters (gw2-api/issues#56). When that happens, the AI says the counts are unknown rather than
  guessing, and the Player can check the in-game Hero panel. Mastery points and tracks come from other
  endpoints and are not affected.
- **API access needs a restart after a build adds API permissions.** On the first load of a build
  that asks for new Guild Wars 2 API permissions, Blish HUD gives the module no API access for that
  session, even though the permissions show as granted. The Chat says so; restarting Blish HUD fixes
  it.

## CI and releases

`.github/workflows/build.yml` runs on every push and pull request (Windows): it builds the solution,
runs the tests, and keeps the `.bhm` as a build artifact. Pushing a `v*` tag additionally attaches
the built `.bhm` to a GitHub release. Tags are maintainer-only: a `v*` tag publishes a release.

## Testing

Tests drive the Chat — the planning core — the way a Player would, and check what a Player would see.
They do not assert on prompt wording or internal calls, with one exception: the identity message has
to name the module, the model and the Provider host and has to forbid claiming to be another product
(`SystemPromptTests`), because a model that is told nothing does exactly that. #5's full system prompt
owns the rest of its wording. The model is faked through `IChatClient`
(`ScriptedChatClient`) and the Provider's HTTP side through a stub message handler
(`StubHttpMessageHandler`), so the tests need no network. Blish HUD itself is replaced by an in-memory
fake world from the tickets that add Lookups.
