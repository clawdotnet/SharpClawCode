using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>Bounds provider-facing tool content without altering the original tool result or its durable events.</summary>
public interface IToolResultContextFormatter
{
    /// <summary>Persists full text before replacing it with a bounded preview and retrieval reference.</summary>
    Task<ContentBlock> PrepareAsync(ContentBlock block, string toolName, ToolExecutionContext context, CancellationToken cancellationToken);
}
