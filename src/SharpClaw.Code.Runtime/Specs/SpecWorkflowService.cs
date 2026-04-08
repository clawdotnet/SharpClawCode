using System.Text;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Specs;

/// <summary>
/// Implements the spec-mode workflow by instructing the model to emit a structured payload and rendering it to markdown artifacts.
/// </summary>
public sealed class SpecWorkflowService(
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock) : ISpecWorkflowService
{
    /// <inheritdoc />
    public string BuildPromptInstructions()
        => """
            Spec mode is active.

            Produce a feature spec and respond with JSON only. Do not include prose before or after the JSON.

            Required JSON shape:
            {
              "requirements": {
                "title": "Short feature title",
                "summary": "One-paragraph summary",
                "requirements": [
                  {
                    "id": "REQ-001",
                    "statement": "When <condition>, the system shall <behavior>.",
                    "rationale": "Optional rationale"
                  }
                ]
              },
              "design": {
                "title": "Short design title",
                "summary": "Brief technical summary",
                "architecture": ["Key architecture point"],
                "dataFlow": ["Important data or execution flow point"],
                "interfaces": ["API, contract, or integration seam"],
                "failureModes": ["Failure case and mitigation"],
                "testing": ["Validation or test strategy point"]
              },
              "tasks": {
                "title": "Short implementation plan title",
                "tasks": [
                  {
                    "id": "TASK-001",
                    "description": "Actionable implementation task",
                    "doneCriteria": "Optional completion criteria"
                  }
                ]
              }
            }

            Requirements must use EARS-style statements containing "shall".
            """;

    /// <inheritdoc />
    public async Task<SpecArtifactSet> MaterializeAsync(
        string workspacePath,
        string userPrompt,
        string modelOutput,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelOutput);

        var payload = DeserializePayload(modelOutput);
        ValidatePayload(payload);

        var datePrefix = systemClock.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var slug = Slugify(userPrompt);
        var root = GetUniqueSpecRoot(workspacePath, datePrefix, slug);
        var requirementsPath = pathService.Combine(root, "requirements.md");
        var designPath = pathService.Combine(root, "design.md");
        var tasksPath = pathService.Combine(root, "tasks.md");

        try
        {
            fileSystem.CreateDirectory(root);
            await fileSystem.WriteAllTextAsync(requirementsPath, RenderRequirements(payload.Requirements), cancellationToken).ConfigureAwait(false);
            await fileSystem.WriteAllTextAsync(designPath, RenderDesign(payload.Design), cancellationToken).ConfigureAwait(false);
            await fileSystem.WriteAllTextAsync(tasksPath, RenderTasks(payload.Tasks), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            fileSystem.DeleteDirectoryRecursive(root);
            throw;
        }

        return new SpecArtifactSet(
            Slug: pathService.GetFileName(root) ?? slug,
            RootPath: pathService.GetCanonicalFullPath(root),
            RequirementsPath: pathService.GetCanonicalFullPath(requirementsPath),
            DesignPath: pathService.GetCanonicalFullPath(designPath),
            TasksPath: pathService.GetCanonicalFullPath(tasksPath),
            GeneratedAtUtc: systemClock.UtcNow);
    }

    private SpecGenerationPayload DeserializePayload(string modelOutput)
    {
        var candidates = new List<string> { modelOutput.Trim() };
        if (TryStripCodeFence(modelOutput) is { } stripped)
        {
            candidates.Add(stripped);
        }

        if (TryExtractJsonObject(modelOutput) is { } extracted)
        {
            candidates.Add(extracted);
        }

        foreach (var candidate in candidates.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var payload = JsonSerializer.Deserialize(candidate, ProtocolJsonContext.Default.SpecGenerationPayload);
                if (payload is not null)
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
                // Try the next candidate.
            }
        }

        throw new InvalidOperationException("Spec mode expected a valid structured JSON response containing requirements, design, and tasks.");
    }

    private static void ValidatePayload(SpecGenerationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Requirements.Title)
            || string.IsNullOrWhiteSpace(payload.Requirements.Summary)
            || payload.Requirements.Requirements.Count == 0
            || payload.Design.Architecture.Count == 0
            || payload.Design.DataFlow.Count == 0
            || payload.Design.Interfaces.Count == 0
            || payload.Design.FailureModes.Count == 0
            || payload.Design.Testing.Count == 0
            || payload.Tasks.Tasks.Count == 0)
        {
            throw new InvalidOperationException("Spec mode output was incomplete. Requirements, design, and tasks must all contain content.");
        }

        if (payload.Requirements.Requirements.Any(static requirement =>
                string.IsNullOrWhiteSpace(requirement.Id)
                || string.IsNullOrWhiteSpace(requirement.Statement)
                || !requirement.Statement.Contains("shall", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Spec mode requirements must include EARS-style requirement statements containing 'shall'.");
        }

        if (payload.Tasks.Tasks.Any(static task => string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Description)))
        {
            throw new InvalidOperationException("Spec mode tasks must contain non-empty ids and descriptions.");
        }
    }

    private string GetUniqueSpecRoot(string workspacePath, string datePrefix, string slug)
    {
        var specsRoot = pathService.Combine(workspacePath, "docs", "superpowers", "specs");
        var folderName = $"{datePrefix}-{slug}";
        var candidate = pathService.Combine(specsRoot, folderName);
        var suffix = 2;
        while (fileSystem.DirectoryExists(candidate))
        {
            candidate = pathService.Combine(specsRoot, $"{folderName}-{suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            suffix++;
        }

        return candidate;
    }

    private static string RenderRequirements(SpecRequirementsDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title).AppendLine();
        builder.AppendLine("## Summary").AppendLine();
        builder.AppendLine(document.Summary.Trim()).AppendLine();
        builder.AppendLine("## Requirements").AppendLine();

        for (var i = 0; i < document.Requirements.Count; i++)
        {
            var requirement = document.Requirements[i];
            builder.Append(i + 1)
                .Append(". **")
                .Append(requirement.Id.Trim())
                .Append("** ")
                .AppendLine(requirement.Statement.Trim());

            if (!string.IsNullOrWhiteSpace(requirement.Rationale))
            {
                builder.Append("   Rationale: ").AppendLine(requirement.Rationale.Trim());
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string RenderDesign(SpecDesignDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title).AppendLine();
        builder.AppendLine("## Summary").AppendLine();
        builder.AppendLine(document.Summary.Trim()).AppendLine();
        AppendBulletSection(builder, "Architecture", document.Architecture);
        AppendBulletSection(builder, "Data Flow", document.DataFlow);
        AppendBulletSection(builder, "Interfaces", document.Interfaces);
        AppendBulletSection(builder, "Failure Modes", document.FailureModes);
        AppendBulletSection(builder, "Testing", document.Testing);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string RenderTasks(SpecTasksDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title).AppendLine();
        builder.AppendLine("## Tasks").AppendLine();

        foreach (var task in document.Tasks)
        {
            builder.Append("- [ ] **")
                .Append(task.Id.Trim())
                .Append("** ")
                .AppendLine(task.Description.Trim());

            if (!string.IsNullOrWhiteSpace(task.DoneCriteria))
            {
                builder.Append("  Done when: ").AppendLine(task.DoneCriteria.Trim());
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendBulletSection(StringBuilder builder, string heading, IEnumerable<string> values)
    {
        builder.Append("## ").AppendLine(heading).AppendLine();
        foreach (var value in values.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            builder.Append("- ").AppendLine(value.Trim());
        }

        builder.AppendLine();
    }

    private static string Slugify(string prompt)
    {
        Span<char> buffer = stackalloc char[Math.Min(prompt.Length, 64)];
        var outputIndex = 0;
        var lastWasDash = false;

        foreach (var character in prompt)
        {
            if (outputIndex == buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                buffer[outputIndex++] = char.ToLowerInvariant(character);
                lastWasDash = false;
                continue;
            }

            if (outputIndex == 0 || lastWasDash)
            {
                continue;
            }

            buffer[outputIndex++] = '-';
            lastWasDash = true;
        }

        while (outputIndex > 0 && buffer[outputIndex - 1] == '-')
        {
            outputIndex--;
        }

        return outputIndex == 0
            ? "spec"
            : new string(buffer[..outputIndex]);
    }

    private static string? TryStripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return null;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return null;
        }

        return trimmed[(firstNewLine + 1)..^3].Trim();
    }

    private static string? TryExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return value[start..(end + 1)].Trim();
    }
}
