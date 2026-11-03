using Microsoft.Extensions.Options;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Runtime.Tools;

/// <summary>Retrieves bounded text windows from the current session's durable tool output.</summary>
public sealed class ReadToolOutputTool(IToolOutputArtifactStore artifacts, IOptions<ToolOutputOptions> options) : SharpClawToolBase
{
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new("read_tool_output", "Read a window of a full tool output artifact from this session.", ApprovalScope.ToolExecution, false, false,
        nameof(ReadToolOutputArguments), "Artifact ID, zero-based UTF-16 offset, and limit of 1-4096 characters.", ["read", "artifact"],
        """{"type":"object","properties":{"artifactId":{"type":"string"},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":4096}},"required":["artifactId"],"additionalProperties":false}""");

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var args = DeserializeArguments<ReadToolOutputArguments>(request);
        if (args.Offset < 0 || args.Limit is < 1 or > 4096) return CreateFailureResult(context, request, "Offset must be nonnegative and limit must be between 1 and 4096.");
        var artifact = await artifacts.GetAsync(context.WorkspaceRoot, context.SessionId, args.ArtifactId, cancellationToken).ConfigureAwait(false);
        if (artifact is null) return CreateFailureResult(context, request, "Output artifact was not found in this session.");
        var start = Math.Min(args.Offset, artifact.Content.Length);
        if (start > 0 && start < artifact.Content.Length && char.IsLowSurrogate(artifact.Content[start])) start--;
        // Budget for worst-case JSON escaping and metadata so retrieval never creates
        // another oversized preview of the very artifact being paged.
        var limit = Math.Min(args.Limit, Math.Max(1, (options.Value.MaxCharacters - 512) / 6));
        var end = (int)Math.Min((long)start + limit, artifact.Content.Length);
        if (end < artifact.Content.Length && end > start && char.IsHighSurrogate(artifact.Content[end - 1]))
            end += end - start == 1 ? 1 : -1;

        var page = new ToolOutputArtifactPage(artifact.Id, start, artifact.Content[start..end], artifact.Content.Length, end < artifact.Content.Length ? end : null);
        return CreateSuccessResult(context, request, page.Content + $"\nNext offset: {page.NextOffset?.ToString() ?? "end"}", page);
    }
}

/// <summary>Reads a bounded character window from an opaque artifact ID in the current session.</summary>
public sealed record ReadToolOutputArguments(string ArtifactId, int Offset = 0, int Limit = 4096);
