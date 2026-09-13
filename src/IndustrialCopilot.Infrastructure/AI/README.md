# LLM providers

The host registers `services.AddLlmProviders(configuration)` from
`IndustrialCopilot.Infrastructure.AI`. It receives one `ILlmProvider` singleton;
the container owns and disposes the concrete HTTP adapters at shutdown.
Configuration is validated during registration, before requests are accepted.
Rebuild the host to change settings; options are immutable snapshots.

Example configuration (no secret):

```json
{
  "Llm": {
    "PrimaryProvider": "OpenAi",
    "FallbackProvider": "Ollama",
    "FallbackEnabled": false,
    "EmbeddingProvider": "OpenAi",
    "OpenAi": {
      "Endpoint": "https://api.openai.com/",
      "ChatModel": "gpt-4.1-mini-2025-04-14",
      "EmbeddingModel": "text-embedding-3-small",
      "RequestTimeoutSeconds": 60,
      "StreamTimeoutSeconds": 120
    },
    "Ollama": {
      "Endpoint": "http://localhost:11434/",
      "ChatModel": "llama3.1:8b",
      "EmbeddingModel": "embeddinggemma",
      "RequestTimeoutSeconds": 120,
      "StreamTimeoutSeconds": 180
    }
  }
}
```

Supply `Llm__OpenAi__ApiKey` through the host environment, or
`Llm:OpenAi:ApiKey` through user secrets/a deployment secret provider.
This library does not load dotenv files or read secrets itself. Local-only
selection needs no OpenAI key. Provider names are `OpenAi` and `Ollama`.
Endpoints must be roots with no credentials/query/fragment; hosted OpenAI is
restricted to its HTTPS API host. Ollama permits HTTPS or loopback HTTP.
Redirects, cookies, retries, and payload logging are disabled in the adapters.

Both adapters implement completion, text streaming, tool-call requests, and
embeddings. Tool calls never execute. Tool results must correlate to the
preceding assistant call by ID and name. Use chat models supporting text and
function calling; reasoning history and multimodal payloads are not modeled
by the current Application contracts. Ollama must be a current installation
supporting compatible tool IDs and streaming usage, with both models installed.

Chat fallback is opt-in and makes at most one second attempt. Only known
transient sockets, adapter deadlines, HTTP 408/500/502/503/504, and explicitly
temporary rate limits are eligible. Authentication, model/request errors,
quota/billing, unknown 429, TLS/permanent DNS, refusals, malformed successful
responses, and unknown transport failures do not trigger fallback. Recognized
configuration/quota codes override a transient HTTP status. Error-body
classification is bounded and never exposes provider text. An unreadable 429
body is not reclassified as a transient timeout. Inference may already have
occurred when a timeout happens, so duplicate inference/billing is possible.

After any streaming chunk is yielded, fallback is forbidden. Streams require
an actual finish event and `[DONE]`; EOF is a failure. SSE events are limited to
1 MiB of characters, with incremental UTF-8 decoding and cancellation-aware
reads. Per-attempt streaming deadlines include time between consumer reads.

Embeddings never fall back. OpenAI response indexes restore input order;
Ollama native `/api/embed` preserves batch order and uses `truncate:false`.
Equal dimensions do not imply compatible embedding spaces. Changing the
embedding provider/model requires a corresponding application-owned
`EmbeddingProfile` and reindexing in the future ingestion workflow.

Normal Infrastructure tests use fake HTTP handlers/streams only. A manual,
opt-in smoke check should exercise all four methods for each installed/model
configuration, including multiple tool calls followed by correlated results,
and cancel a stream mid-response. It requires a hosted key or running Ollama
as appropriate; normal CI does not. No live smoke call has been performed as
part of these offline tests.
