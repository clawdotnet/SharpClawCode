using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.CustomCommands;

/// <inheritdoc />
public sealed class CustomCommandMarkdownParser : ICustomCommandMarkdownParser
{
    /// <inheritdoc />
    public (CustomCommandDefinition? Definition, IReadOnlyList<CustomCommandDiscoveryIssue> Issues) Parse(
        string name,
        string absolutePath,
        CustomCommandSourceScope scope,
        string markdownText)
    {
        var issues = new List<CustomCommandDiscoveryIssue>();
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new CustomCommandDiscoveryIssue(absolutePath, "Command name is required."));
            return (null, issues);
        }

        string body;
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (markdownText.TrimStart().StartsWith("---", StringComparison.Ordinal))
        {
            var end = markdownText.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0)
            {
                issues.Add(new CustomCommandDiscoveryIssue(absolutePath, "Frontmatter is missing a closing '---' line."));
                return (null, issues);
            }

            var fm = markdownText[3..end];
            body = markdownText[(end + "\n---".Length)..].TrimStart();
            foreach (var rawLine in fm.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                meta[key] = value;
            }
        }
        else
        {
            body = markdownText;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            issues.Add(new CustomCommandDiscoveryIssue(absolutePath, "Command template body is empty."));
            return (null, issues);
        }

        meta.TryGetValue("description", out var description);
        meta.TryGetValue("agent", out var agent);
        if (!string.IsNullOrWhiteSpace(agent)
            && string.Equals(agent.Trim(), SubAgentWorker.SubAgentId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                new CustomCommandDiscoveryIssue(
                    absolutePath,
                    $"Agent '{SubAgentWorker.SubAgentId}' requires a delegated task contract and is not supported from custom command markdown yet."));
            return (null, issues);
        }

        meta.TryGetValue("model", out var model);
        PermissionMode? permission = PermissionModeText.TryParseOptional(meta.GetValueOrDefault("permissionMode"));
        PrimaryMode? primary = PrimaryModeText.TryParseOptional(meta.GetValueOrDefault("primaryMode"));

        List<string>? tags = null;
        if (meta.TryGetValue("tags", out var tagsText) && !string.IsNullOrWhiteSpace(tagsText))
        {
            tags = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "description",
            "agent",
            "model",
            "permissionMode",
            "primaryMode",
            "arguments",
            "tags",
        };
        Dictionary<string, string>? extensions = null;
        foreach (var pair in meta)
        {
            if (reserved.Contains(pair.Key))
            {
                continue;
            }

            extensions ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            extensions[pair.Key] = pair.Value;
        }

        Dictionary<string, string>? argsMeta = null;
        if (meta.TryGetValue("arguments", out var argsLine) && !string.IsNullOrWhiteSpace(argsLine))
        {
            argsMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hint"] = argsLine,
            };
        }

        var def = new CustomCommandDefinition(
            Name: name,
            Description: description,
            AgentId: string.IsNullOrWhiteSpace(agent) ? null : agent,
            Model: string.IsNullOrWhiteSpace(model) ? null : model,
            PermissionMode: permission,
            PrimaryModeOverride: primary,
            Arguments: argsMeta,
            Tags: tags,
            TemplateBody: body,
            SourcePath: absolutePath,
            SourceScope: scope,
            ExtensionMetadata: extensions);

        return (def, issues);
    }
}
