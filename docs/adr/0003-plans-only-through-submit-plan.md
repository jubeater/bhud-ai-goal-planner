# The AI can only propose changes, through a single `submit_plan` tool

Every tool the AI can call is a read-only Lookup, except `submit_plan`, which delivers the Plan. Nothing the AI calls changes anything directly. A Plan only takes effect through validation, Review (or Auto-apply) and Apply. We use a tool call for the Plan rather than structured JSON output, because structured output support varies across Providers, and every model we accept can call tools. Tools added from outside later (e.g. via MCP) are always read-only.

## Considered Options

- **Structured output (`{reply, actions}` JSON).** Clean on Claude, but unreliable or missing on many OpenAI-compatible and local models.
- **One tool per change kind that acts immediately.** Simpler loop, but it would make changes before the Player sees them.

## Consequences

The planning core (tool loop, Plan handling) has no dependency on the UI, so an MCP server or other front end can use it later.
