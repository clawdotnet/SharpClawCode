# Providers

Model access is through **`IModelProvider`** (`src/SharpClaw.Code.Providers/Abstractions/IModelProvider.cs`):

- **`ProviderName`** — stable id used by **`IModelProviderResolver`**
- **`GetAuthStatusAsync`**
- **`StartStreamAsync`** — returns **`ProviderStreamHandle`** with **`IAsyncEnumerable<ProviderEvent>`**

Registered implementations (see **`ProvidersServiceCollectionExtensions`**):

- **`AnthropicProvider`** — HTTP client from **`AnthropicProviderOptions`**
- **`OpenAiCompatibleProvider`** — HTTP client from **`OpenAiCompatibleProviderOptions`**

Both are registered as **`IModelProvider`** singletons; **`ModelProviderResolver`** builds a case-insensitive dictionary by **`ProviderName`**.

## Resolution and preflight

**`ProviderRequestPreflight`** (`IProviderRequestPreflight`) normalizes **`ProviderRequest`**:

- Applies **`ProviderCatalogOptions.ModelAliases`** (e.g. `"default"` → provider + model id).
- Supports qualified model forms (implementation parses `provider/model`).
- Fills default **provider name** from **`ProviderCatalogOptions.DefaultProvider`** when missing.

Default catalog (**`ProviderCatalogOptions`**) uses **`DefaultProvider = "openai-compatible"`** if not configured.

## Configuration sections

When using **`AddSharpClawRuntime(IConfiguration)`** (CLI host):

| Section | Options type |
|---------|----------------|
| `SharpClaw:Providers:Catalog` | **`ProviderCatalogOptions`** |
| `SharpClaw:Providers:Anthropic` | **`AnthropicProviderOptions`** (`ProviderName` defaults to `"anthropic"`, `BaseUrl`, API key binding as in options class) |
| `SharpClaw:Providers:OpenAiCompatible` | **`OpenAiCompatibleProviderOptions`** (`ProviderName` defaults to `"openai-compatible"`) |

There is no checked-in **`appsettings.json`** in the repo; add one next to the CLI project or rely on environment variables / user secrets per standard .NET configuration.

## Auth

**`IAuthFlowService`** / **`AuthFlowService`** answer whether a provider name is authenticated (used by **`ProviderBackedAgentKernel`**). If not authenticated, the kernel may return a **placeholder** completion (see kernel logs) rather than calling the remote API.

Hard failures use **`ProviderExecutionException`** with **`ProviderFailureKind`**: **`MissingProvider`**, **`AuthenticationUnavailable`**, **`StreamFailed`**.

## Adding a provider

1. Implement **`IModelProvider`** (stream events using **`ProviderEvent`** from **Protocol**).
2. Register the implementation in **`ProvidersServiceCollectionExtensions`** (or a test **`IServiceCollection`**) as **`AddSingleton<IModelProvider, YourProvider>`**.
3. Ensure **`ModelProviderResolver`** can resolve your **`ProviderName`** (unique among registered providers).
4. Extend **`ProviderCatalogOptions`** (defaults / aliases) via **`IConfiguration`** or **`Configure<ProviderCatalogOptions>`** / **`PostConfigure`**.

**Test pattern:** **`SharpClaw.Code.MockProvider`** registers **`DeterministicMockModelProvider`** with **`PostConfigure<ProviderCatalogOptions>`** so **`default`** maps to provider name **`mock`** (`MockProviderServiceCollectionExtensions`).
