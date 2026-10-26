# Prefer Installed Modules; install from the Catalog only to fill a named Gap

A capable AI will tend to suggest the "best" Module for a goal even when the Player already has one that does the job, which piles up overlapping Modules. The AI must use Installed Modules first, enabling a disabled one if needed. It may propose an install only when no Installed Module can do the job. The install Action must name the Gap, and it may never add a Module whose function overlaps with one installed or already in the Plan. Installs come only from the official Catalog, through Blish HUD's own installer with its hash check, never from a URL the AI suggests, because an installed Module runs with full trust in Blish HUD's process.

## Consequences

The Catalog has no categories, so overlap is judged by the AI from Module descriptions. The validator can only check that a Gap is stated and that the same Module isn't proposed twice. An installed Module that looks abandoned may be mentioned as Advice, never as an install Action.
