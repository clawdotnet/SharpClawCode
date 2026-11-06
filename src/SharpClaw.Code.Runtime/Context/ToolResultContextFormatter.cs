using Microsoft.Extensions.Options;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>Keeps a bounded head and tail in model context and saves the complete tool text for retrieval.</summary>
public sealed class ToolResultContextFormatter(IToolOutputArtifactStore artifacts, IOptions<ToolOutputOptions> options) : IToolResultContextFormatter
{
    /// <inheritdoc />
    public async Task<ContentBlock> PrepareAsync(ContentBlock block, string toolName, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (block.Text is not { } text || text.Length <= options.Value.MaxCharacters) return block;
        var artifact = await artifacts.SaveAsync(context.WorkspaceRoot, context.SessionId, context.TurnId, toolName, text, cancellationToken).ConfigureAwait(false);
        var marker = $"\n[Output shortened from {text.Length} characters. Full output: read_tool_output with artifactId=\"{artifact.Id}\", offset=0, limit=4096. Offsets count UTF-16 characters.]\n";
        var remaining = options.Value.MaxCharacters - marker.Length;
        var head = (int)(remaining * 3L / 4);
        var tail = remaining - head;
        if (char.IsHighSurrogate(text[head - 1])) head--;
        if (char.IsLowSurrogate(text[text.Length - tail])) tail--;
        return block with { Text = text[..head] + marker + text[^tail..] };
    }
}
