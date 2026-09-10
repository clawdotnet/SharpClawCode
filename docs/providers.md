# Providers

Model access is through **`IModelProvider`** (`src/SharpClaw.Code.Providers/Abstractions/IModelProvider.cs`):

- **`ProviderName`** — stable id used by **`IModelProviderResolver`**
- **`GetAuthStatusAsync`**
- **`StartStreamAsync`** — returns **`ProviderStreamHandle`** with **`IAsyncEnumerable<ProviderEvent>`**

Registered implementations (see **`ProvidersServiceCollectionExtensions`**):

- **`AnthropicProvider`** — HTTP client from **`AnthropicProviderOptions`**
- **`OpenAiCompatibleProvider`** — HTTP client from **`OpenAiCompatibleProviderOptions`**

Both are registered as **`IModelProvider`** singletons; **`ModelProviderResolver`** builds a case-insensitive dictionary by **`ProviderName`** and returns an ordered primary/fallback candidate chain.

The provider layer also exposes **`IProviderCatalogService`**, which powers the CLI `models` command and ACP `models/list`. It centralizes:

- provider auth status
- alias/default resolution
- discovered local runtime profiles
- model discovery for local runtimes

## Resolution and preflight

**`ProviderRequestPreflight`** (`IProviderRequestPreflight`) normalizes **`ProviderRequest`**:

- Applies **`ProviderCatalogOptions.ModelAliases`** (e.g. `"default"` → provider + model id).
- Supports qualified model forms (implementation parses `provider/model`).
- Also supports local runtime forms such as `ollama/qwen2.5-coder`, which route through the OpenAI-compatible provider with profile metadata attached.
- Fills default **provider name** from **`ProviderCatalogOptions.DefaultProvider`** when missing.

Default catalog (**`ProviderCatalogOptions`**) uses **`DefaultProvider = "openai-compatible"`** if not configured.

Configure **`FallbackProviders`** as an ordered list of registered provider names. The runtime authenticates candidates before use, buffers each provider iteration until it completes, and advances to the next candidate on a failed stream. Buffering prevents partial primary output from leaking into the fallback response or executing a tool twice.

Provider resilience is configured under **`SharpClaw:Providers:Resilience`**. Its timeout covers both request startup and async stream enumeration. Transient failures are retried only before the stream emits its first event; once output exists, the attempt fails without replay to avoid duplicate deltas. Repeated failures open the circuit breaker.

## Configuration sections

When using **`AddSharpClawRuntime(IConfiguration)`** (CLI host):

| Section | Options type |
|---------|----------------|
| `SharpClaw:Providers:Catalog` | **`ProviderCatalogOptions`** |
| `SharpClaw:Providers:Anthropic` | **`AnthropicProviderOptions`** (`ProviderName` defaults to `"anthropic"`, `BaseUrl`, API key binding as in options class) |
| `SharpClaw:Providers:OpenAiCompatible` | **`OpenAiCompatibleProviderOptions`** (`ProviderName` defaults to `"openai-compatible"`, supports auth mode, default embedding model, and named `LocalRuntimes`) |
| `SharpClaw:Providers:Resilience` | **`ProviderResilienceOptions`** (retry, backoff, timeout, and circuit-breaker settings) |

Example fallback and resilience configuration:

```json
{
  "SharpClaw": {
    "Providers": {
      "Catalog": {
        "DefaultProvider": "anthropic",
        "FallbackProviders": ["openai-compatible"],
        "FallbackModels": {
          "openai-compatible": "gpt-4.1-mini"
        }
      },
      "Resilience": {
        "MaxRetries": 3,
        "RequestTimeout": "00:05:00",
        "CircuitBreakerFailureThreshold": 5,
        "CircuitBreakerBreakDuration": "00:00:30"
      }
    }
  }
}
```

There is no checked-in **`appsettings.json`** in the repo; add one next to the CLI project or rely on environment variables / user secrets per standard .NET configuration.

## Local runtimes

`OpenAiCompatibleProviderOptions.LocalRuntimes` supports named profiles for local or self-hosted runtimes such as Ollama and llama.cpp.

Each profile carries:

- runtime kind (`Generic`, `Ollama`, `LlamaCpp`)
- base URL
- default chat model
- optional default embedding model
- auth mode (`ApiKey`, `Optional`, `None`)
- capability hints for tool calling and embeddings

At runtime the catalog service probes these profiles and surfaces health plus discovered models. Local runtimes do not assume API-key auth by default.

## Auth

**`IAuthFlowService`** / **`AuthFlowService`** answer whether a provider name is authenticated (used by **`ProviderBackedAgentKernel`**). Unauthenticated or expired candidates are skipped when a fallback exists; if none is available, the turn fails with a classified exception instead of returning synthetic provider output.

For the OpenAI-compatible provider, auth status now respects provider auth mode plus any configured auth-optional local runtimes.

Hard failures use **`ProviderExecutionException`** with **`ProviderFailureKind`**: **`MissingProvider`**, **`AuthenticationUnavailable`**, **`StreamFailed`**.

## Adding a provider

1. Implement **`IModelProvider`** (stream events using **`ProviderEvent`** from **Protocol**).
2. Register the implementation in **`ProvidersServiceCollectionExtensions`** (or a test **`IServiceCollection`**) as **`AddSingleton<IModelProvider, YourProvider>`**.
3. Ensure **`ModelProviderResolver`** can resolve your **`ProviderName`** (unique among registered providers).
4. Extend **`ProviderCatalogOptions`** (defaults / aliases) via **`IConfiguration`** or **`Configure<ProviderCatalogOptions>`** / **`PostConfigure`**.

**Test pattern:** **`SharpClaw.Code.MockProvider`** registers **`DeterministicMockModelProvider`** with **`PostConfigure<ProviderCatalogOptions>`** so **`default`** maps to provider name **`mock`** (`MockProviderServiceCollectionExtensions`).
