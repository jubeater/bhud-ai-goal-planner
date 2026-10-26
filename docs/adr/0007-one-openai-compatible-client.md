---
status: accepted
supersedes: ADR-0002
---

# One OpenAI-compatible client for every Provider

The maintainer and many Players want to use whatever model they already pay for, such as DeepSeek, Ollama Cloud's free models, OpenAI or a local model, not specifically Claude. So the module connects to every Provider the same way: an endpoint URL, an API key (optional for local endpoints) and a model name, through a single OpenAI-compatible client, using the Chat Completions API, behind `Microsoft.Extensions.AI` `IChatClient`. There's no Provider-specific SDK and no built-in default Provider. Claude stays usable through Anthropic's OpenAI-compatible endpoint.

## Considered Options

- **Claude through its own SDK, plus an OpenAI-compatible client (ADR-0002).** Gives Claude-only features (prompt caching control, refusal fallback, adaptive thinking settings). But it means two code paths and twice the libraries to get loading inside Blish HUD, for features the maintainer won't test day to day.
- **Using a ChatGPT subscription.** Rejected: a subscription isn't an API for third-party apps.

## Consequences

- Models differ a lot in how well they call tools. The tool-calling test on first use, the strict `submit_plan` schema and the validator matter more, because no single tested Provider anchors quality.
- Tokens remain the usage unit, with cached tokens shown only when the Provider reports them.
- Optional settings are passed through only if the endpoint accepts them.
