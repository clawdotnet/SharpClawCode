using System.Text.Json;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.Runtime.Verification;

/// <summary>Runs one initial check plus the configured repair budget, durably consuming attempts before agent execution.</summary>
public sealed class VerificationLoopCoordinator(IToolExecutor tools, IEventStore events, IRuntimeEventPublisher publisher) : IVerificationLoopCoordinator
{
    /// <inheritdoc />
    public async Task<VerificationLoopResult> RunAsync(ISharpClawAgent agent, AgentRunContext context, AgentRunResult initial, TurnMutationAccumulator mutations, VerificationPolicy policy, CancellationToken cancellationToken)
    {
        var invocations = new List<ProviderInvocationRecord>();
        AddInvocation(initial);
        var published = new List<RuntimeEvent>();
        if (!policy.Enabled || !policy.RunAfterMutatingTurn || context.PrimaryMode != PrimaryMode.Build || !mutations.ToSnapshot().Any(operation => Supported(operation.RelativePath))) return new(initial, null, invocations.ToArray(), []);
        var prior = string.IsNullOrWhiteSpace(context.SessionId) ? [] : (await events.ReadAllAsync(context.WorkingDirectory, context.SessionId, cancellationToken).ConfigureAwait(false)).Where(item => item.TurnId == context.TurnId).ToArray();
        var initialStart = prior.OfType<VerificationStartedEvent>().FirstOrDefault();
        var limit = initialStart is null ? policy.MaxRepairIterations : Math.Min(initialStart.MaxRepairIterations, policy.MaxRepairIterations);
        var consumed = prior.OfType<VerificationRepairStartedEvent>().Select(item => item.Attempt).DefaultIfEmpty(0).Max();
        var completed = prior.OfType<VerificationCompletedEvent>().LastOrDefault();
        var aggregate = initial;
        var report = completed?.Report;
        var history = new List<ChatMessage>(context.ConversationHistory ?? []);
        history.Add(new("user", context.UserContent ?? [Text(context.Prompt)]));
        if (report is null) report = await VerifyAsync(consumed).ConfigureAwait(false);
        while (report.Status == VerificationStatus.Failed && report.Reason is VerificationFailureReason.BuildFailed or VerificationFailureReason.TestFailed && consumed < limit)
        {
            var attempt = ++consumed;
            await PublishAsync(new VerificationRepairStartedEvent(Id(), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, attempt), cancellationToken).ConfigureAwait(false);
            history.Add(new("assistant", [Text(DotNetDiagnosticParser.Bound(aggregate.Output, 4000))]));
            if (aggregate.ToolResults is { Count: > 0 }) history.Add(new("user", [Text("Prior tool results: " + DotNetDiagnosticParser.Bound(string.Join('\n', aggregate.ToolResults.Select(result => result.ToolName + ": " + (result.StructuredOutputJson ?? result.Output ?? result.ErrorMessage))), 6000))]));
            var prompt = VerificationFailureContextFormatter.Format(context.Prompt, aggregate.Output, report, attempt, limit);
            try
            {
                var repaired = await agent.RunAsync(context with { Prompt = prompt, ConversationHistory = history.ToArray(), UserContent = [Text(prompt)], ToolMutationRecorder = mutations }, cancellationToken).ConfigureAwait(false);
                AddInvocation(repaired);
                aggregate = Aggregate(aggregate, repaired);
                await PublishAsync(new VerificationRepairCompletedEvent(Id(), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, attempt, true, repaired.Summary), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                await PublishAsync(new VerificationRepairCompletedEvent(Id(), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, attempt, false, DotNetDiagnosticParser.Bound(failure.Message, 2000)), cancellationToken).ConfigureAwait(false);
                report = report with { Status = VerificationStatus.Failed, Reason = VerificationFailureReason.ProcessFailed, Summary = "Repair agent failed: " + DotNetDiagnosticParser.Bound(failure.Message, 2000), RepairAttempts = consumed };
                break;
            }
            report = await VerifyAsync(attempt).ConfigureAwait(false);
        }
        report = report with { RepairAttempts = consumed };
        aggregate = aggregate with { Summary = aggregate.Summary + " Verification: " + report.Status + ". " + report.Summary };
        return new(aggregate, report, invocations.ToArray(), published.ToArray());

        void AddInvocation(AgentRunResult result) { if (result.ProviderRequest is not null) invocations.Add(new(result.ProviderRequest, (result.ProviderEvents ?? []).ToArray())); }
        async Task<VerificationRunReport> VerifyAsync(int attempt)
        {
            if (!prior.OfType<VerificationStartedEvent>().Any(item => item.Attempt == attempt)) await PublishAsync(new VerificationStartedEvent(Id(), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, attempt, policy.Scope, limit), cancellationToken).ConfigureAwait(false);
            var arguments = new VerifyWorkspaceArguments(policy.Scope, Restore: policy.AllowRestore, ChangedPaths: mutations.ToSnapshot().Select(operation => operation.RelativePath).Distinct(StringComparer.Ordinal).ToArray());
            var toolContext = new ToolExecutionContext(context.SessionId, context.TurnId, context.WorkingDirectory, context.WorkingDirectory, EffectiveMode(context), OutputFormat.Json, null, context.Model, agent.AgentId, context.Metadata,
                AllowedTools: Names(SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson), IsInteractive: context.IsInteractive, SourceKind: PermissionRequestSourceKind.Runtime,
                TrustedPluginNames: Names(SharpClawWorkflowMetadataKeys.TrustedPluginNamesJson), TrustedMcpServerNames: Names(SharpClawWorkflowMetadataKeys.TrustedMcpServerNamesJson), PrimaryMode: context.PrimaryMode, MutationRecorder: mutations, ApprovalSettings: context.ApprovalSettings);
            var envelope = await tools.ExecuteAsync("verify_workspace", JsonSerializer.Serialize(arguments, ProtocolJsonContext.Default.VerifyWorkspaceArguments), toolContext, cancellationToken).ConfigureAwait(false);
            aggregate = aggregate with { ToolResults = [.. aggregate.ToolResults ?? [], envelope.Result] };
            var now = DateTimeOffset.UtcNow;
            var verified = envelope.Result.StructuredOutputJson is { } json ? JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.VerificationRunReport) : null;
            verified ??= new(context.WorkingDirectory, null, policy.Scope, VerificationStatus.Failed, envelope.PermissionDecision.IsAllowed ? VerificationFailureReason.ProcessFailed : VerificationFailureReason.PermissionDenied, now, now, 0, [], null, null, null, envelope.Result.ErrorMessage ?? "Verification failed.");
            verified = verified with { RepairAttempts = consumed };
            await PublishAsync(new VerificationCompletedEvent(Id(), context.SessionId, context.TurnId, now, attempt, verified), cancellationToken).ConfigureAwait(false);
            return verified;
        }
        string[]? Names(string key) => context.Metadata?.TryGetValue(key, out var json) == true ? JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.StringArray) : null;
        async Task PublishAsync(RuntimeEvent runtimeEvent, CancellationToken token)
        {
            await publisher.PublishAsync(runtimeEvent, new RuntimeEventPublishOptions(context.WorkingDirectory, context.SessionId, PersistToSessionStore: !string.IsNullOrWhiteSpace(context.SessionId), ThrowIfPersistenceFails: !string.IsNullOrWhiteSpace(context.SessionId)), token).ConfigureAwait(false);
            published.Add(runtimeEvent);
        }
    }
    private static AgentRunResult Aggregate(AgentRunResult earlier, AgentRunResult later)
        => later with
        {
            Usage = new(earlier.Usage.InputTokens + later.Usage.InputTokens, earlier.Usage.OutputTokens + later.Usage.OutputTokens, earlier.Usage.CachedInputTokens + later.Usage.CachedInputTokens, earlier.Usage.TotalTokens + later.Usage.TotalTokens, earlier.Usage.EstimatedCostUsd is null || later.Usage.EstimatedCostUsd is null ? null : earlier.Usage.EstimatedCostUsd + later.Usage.EstimatedCostUsd),
            ProviderEvents = [.. earlier.ProviderEvents ?? [], .. later.ProviderEvents ?? []], ToolResults = [.. earlier.ToolResults ?? [], .. later.ToolResults ?? []], Events = [.. earlier.Events ?? [], .. later.Events ?? []]
        };
    private static PermissionMode EffectiveMode(AgentRunContext context) => context.Metadata?.TryGetValue(SharpClawWorkflowMetadataKeys.PreferredPermissionMode, out var value) == true && Enum.TryParse<PermissionMode>(value, true, out var mode) ? (PermissionMode)Math.Min((int)mode, (int)context.PermissionMode) : context.PermissionMode;
    private static bool Supported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".csproj" or ".sln" or ".slnx" or ".props" or ".targets" or ".razor" || Path.GetFileName(path) is "global.json" or "packages.lock.json";
    private static ContentBlock Text(string text) => new(ContentBlockKind.Text, text, null, null, null, null);
    private static string Id() => "event-" + Guid.NewGuid().ToString("N");
}
