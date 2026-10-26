---
status: superseded by ADR-0007
---

# Swappable Providers through Microsoft.Extensions.AI `IChatClient`

Players bring their own key, and some will want a Provider other than Claude, including local models. We talk to every Provider through `IChatClient`: Claude through the official Anthropic SDK, and anything else through one OpenAI-compatible client. We chose this over a Claude-only integration and over writing our own provider interface. Claude stays the default and the only tested Provider. Claude-only features (prompt caching, refusal fallback) are switched on only when Claude is in use.

## Considered Options

- **Claude only, with the native SDK.** Simplest, but players couldn't use other Providers or local models.
- **Our own interface, with a hand-written client per Provider.** Full control, but we'd be re-implementing tool calling and streaming that `IChatClient` already provides. The official MCP C# SDK is also built on `IChatClient`, so this choice keeps MCP easy to add later.

## Consequences

Usage is tracked in tokens, not money, because prices differ by Provider. Any model that can't call tools is refused at setup.
