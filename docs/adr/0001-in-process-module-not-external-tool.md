# Run as a Blish HUD module, not an external tool that edits settings.json

Blish HUD has no external API, so the alternative was a separate app that rewrites `settings.json`. We run inside Blish HUD as a module instead. Only in-process can we see Setting labels, descriptions and ranges, apply changes while Blish HUD is running (it overwrites `settings.json` every few seconds), enable or disable Modules, and use Blish HUD's own installer and GW2 API access.

## Consequences

We depend on Blish HUD's public classes and run on `net48` with Blish HUD's already-loaded libraries. Every third-party dependency must be checked to load inside its process.
