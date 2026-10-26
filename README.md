# AI Goal Planner

A [Blish HUD](https://blishhud.com/) module in which an AI sets up and configures your other Blish HUD
modules to reach a goal you describe in chat. The design is tracked as GitHub issues on this repo;
`GLOSSARY.md` and `docs/adr/` explain the terms and the decisions behind it.

**Status:** the module skeleton. A corner icon opens the Chat window, and Send echoes your Request
back. The AI is not connected yet (ticket #3).

## Build

Needs the .NET SDK (8.0 or later) on Windows. No Visual Studio required.

```
dotnet build
dotnet test
```

`dotnet build` produces the module as `src/AiGoalPlanner/bin/<Configuration>/net48/AiGoalPlanner.bhm`
(Debug and Release). The `.bhm` is a zip of `AiGoalPlanner.dll`, its `.pdb`, `manifest.json` and the
module's own `ref/` content. Blish HUD's own assemblies are referenced from the `BlishHUD` NuGet
package and are never bundled into the module, because Blish HUD already has them loaded.

## Debug in VS Code

`.vscode/launch.json` starts Blish HUD with this module loaded, so breakpoints hit:

- Blish HUD is expected at `%USERPROFILE%\Desktop\Blish.HUD\Blish HUD.exe`; edit `program` and
  `cwd` in `.vscode/launch.json` if yours is elsewhere.
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
They never assert on prompt wording or internal calls. From later tickets on, the model is faked
through `IChatClient` and Blish HUD is replaced by an in-memory fake world.
