# AI Goal Planner

A [Blish HUD](https://blishhud.com/) module in which an AI sets up and configures your other Blish HUD
modules to reach a goal you describe in chat. The design is tracked as GitHub issues on this repo;
`GLOSSARY.md` and `docs/adr/` explain the terms and the decisions behind it.

**Status:** the Chat talks to a real Provider. A corner icon opens the Chat window, **Provider
settings** stores the endpoint, the model and (optionally) the API key, and a Send comes back as a
real AI reply. There are no Lookups or Plans yet, so the AI can only answer in words.

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
output folder is removed too, so one build never packs the previous zip into the new one.

Building while Blish HUD is running can fail on a locked `.bhm`; send the zip elsewhere instead:
`dotnet build -p:BlishHUDModuleOutputDir=obj\bhm\`.

## Connecting a Provider

The Chat talks to any Provider that speaks the OpenAI-compatible **Chat Completions** API (ADR-0007),
so Ollama, DeepSeek, OpenAI and the rest work with only the endpoint, the model and the key changed:

1. Open the Chat window from the corner icon, then click **Provider settings**.
2. Enter the **endpoint URL**: `https://ollama.com/v1`, `https://api.deepseek.com` or, for a local
   Ollama, `http://localhost:11434/v1`. A base URL is enough; `/chat/completions` is appended.
3. Enter the **model name**: `deepseek-chat`, `beam:cloud`, `qwen3.5:9b`.
4. Enter the **API key** if the Provider needs one, and leave it empty for a local Ollama.
5. Click **Save**. The next Request uses these settings; the line under the input row says which
   Provider the Chat will talk to.

The key never goes into Blish HUD's `settings.json`, and it is never logged. It is encrypted with
Windows DPAPI for the current Windows user and written to the module's data folder as one
`provider-<hash>.key` file per endpoint — the endpoint is hashed into the file name, so the URL is
not written next to it. **Forget key** deletes the file. The window never reads a saved key back
into the field: the field starts empty, says when a key is saved, and saving with an empty field
leaves the stored key untouched.

Blish HUD 1.3.0 has no masked text box, so the key is visible while it is typed. Everything after
that keeps it out of sight: not in the settings file, not in the log, never redisplayed.

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
  `Newtonsoft.Json`, which Blish HUD already loads (13.0.1), so no extra JSON library is needed and
  the module never touches `System.Text.Json` itself.
- **`Microsoft.Extensions.AI.Abstractions` is the one library Blish HUD does not have.** The `.bhm`
  therefore adds that assembly and its own dependency closure, and nothing else.

`CopyLocalLockFileAssemblies` is on so those libraries travel in the `.bhm`. That is the documented
way to ship a module dependency: Blish HUD loads an assembly packaged with the module when the module
references it. Blish HUD's own assemblies (`Gw2Sharp`, `MonoGame`, `SharpDX`, `Newtonsoft.Json`,
...) are deleted before the zip, so the `.bhm` carries nothing Blish HUD already has.

The known risk is that a process holds one version per assembly name
([blish-hud/Blish-HUD#740](https://github.com/blish-hud/Blish-HUD/issues/740)): the
`System.Text.Json` that `Microsoft.Extensions.AI.Abstractions` depends on can collide with the copy
Blish HUD loads for Gw2Sharp, or with one that another module ships. The Chat only passes plain text,
so the JSON surface this module actually reaches is small. If a collision turns up in game, the next
step is to leave `System.Text.Json.dll` and the rest of that tree out of the module output and let
Blish HUD's copy serve the abstractions.

**In game:** enable the module in Blish HUD 1.3.0, open the Chat and send a Request. A library that
fails to load shows up in `Documents\Guild Wars 2\addons\blishhud\logs` rather than as a build error;
that failure mode is what the packaging above is there to prevent.

## Debug in VS Code

`.vscode/launch.json` starts Blish HUD with this module loaded, so breakpoints hit:

- Blish HUD is expected at `C:\Users\Tianlin\Desktop\Blish.HUD\Blish HUD.exe`; edit `program` in
  `.vscode/launch.json` if yours is elsewhere.
- The configuration builds the module first (`tasks.json`), then runs
  `Blish HUD.exe --debug --module <path to AiGoalPlanner.bhm>`. `--debug` makes Blish HUD rethrow
  module exceptions to the debugger.
- Logs are written to `Documents\Guild Wars 2\addons\blishhud\logs`.

## CI and releases

`.github/workflows/build.yml` runs on every push and pull request (Windows): it builds the solution,
runs the tests, and keeps the `.bhm` as a build artifact. Pushing a `v*` tag additionally attaches
the built `.bhm` to a GitHub release. Tags are maintainer-only: a `v*` tag publishes a release.

## Testing

Tests drive the Chat — the planning core — the way a Player would, and check what a Player would see.
They never assert on prompt wording or internal calls. The model is faked through `IChatClient`
(`ScriptedChatClient`) and the Provider's HTTP side through a stub message handler
(`StubHttpMessageHandler`), so the tests need no network. Blish HUD itself is replaced by an in-memory
fake world from the tickets that add Lookups.
