---
status: accepted
---

# Stay on Blish HUD's System.Text.Json 5.0

Blish HUD 1.3.0 embeds System.Text.Json 5.0 inside `Blish HUD.exe` and loads it before any module, so every module runs on 5.0 whatever version it ships. `Microsoft.Extensions.AI.Abstractions` 10.11 is built against System.Text.Json 10, and any path that calls a newer API throws `MissingMethodException` at runtime. PR #19 found this in game: `AIFunctionFactory.Create` crashed all of Blish HUD from the module's `Initialize()`. We keep `Microsoft.Extensions.AI` as the seam (ADR-0007), but use only the parts that work on 5.0, and CI checks that against Blish HUD's own assemblies.

## Rules

- Tools are hand-written `AIFunction` subclasses with literal JSON schemas (`JsonDocument.Parse`). No `AIFunctionFactory.Create`, and nothing that reads `AIFunction.JsonSerializerOptions`, `AIJsonUtilities` defaults or other serializer-option defaults: they need System.Text.Json 8 or later.
- JSON on the wire stays on the `Newtonsoft.Json` Blish HUD ships. `JsonElement` is used only through APIs 5.0 has, such as `GetRawText()`.
- A CI test runs the Chat, with tools, against the System.Text.Json 5.0 and companion assemblies extracted from the `Blish HUD.exe` in the BlishHUD NuGet package. Unit tests run on System.Text.Json 10 and can't catch these failures.
- `Initialize()` catches load failures and logs them, so a broken dependency disables this module instead of crashing Blish HUD for every module.

## Considered Options

- **Bundle our own copy (ILRepack `Microsoft.Extensions.AI.Abstractions`, System.Text.Json 10 and their dependencies into the module's assembly).** Robust once it works, but adds a build step, grows the `.bhm`, and is untested with Blish HUD's module loader. Deferred until a dependency we actually need, such as an MCP SDK, can't live within 5.0.
- **Drop `Microsoft.Extensions.AI` for our own types on `Newtonsoft.Json`.** No collision at all, but it gives up the `IChatClient` seam ADR-0007 relies on, and MCP would bring the same problem back later.

## Consequences

- The working API surface is found by testing, not by reading package docs. A `Microsoft.Extensions.AI` upgrade must pass the System.Text.Json 5.0 CI test before it's merged.
- If Blish HUD ships a newer System.Text.Json, these limits can be relaxed, and this ADR superseded.
