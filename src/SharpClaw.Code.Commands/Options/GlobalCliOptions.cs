using System.CommandLine;
using System.CommandLine.Parsing;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands.Options;

/// <summary>
/// Defines and resolves the shared global CLI options.
/// </summary>
public sealed class GlobalCliOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalCliOptions"/> class.
    /// </summary>
    public GlobalCliOptions()
    {
        OutputFormatOption = new Option<string>("--output-format")
        {
            Description = "Selects the output format: text or json.",
            DefaultValueFactory = _ => "text",
            Recursive = true
        };

        WorkingDirectoryOption = new Option<string?>("--cwd")
        {
            Description = "Overrides the working directory used for command execution.",
            Recursive = true
        };

        ModelOption = new Option<string?>("--model")
        {
            Description = "Specifies the model identifier used by prompt execution.",
            Recursive = true
        };

        PermissionModeOption = new Option<string>("--permission-mode")
        {
            Description = "Sets the permission mode: readOnly, workspaceWrite, or dangerFullAccess.",
            DefaultValueFactory = _ => "workspaceWrite",
            Recursive = true
        };

        AutoApproveOption = new Option<string?>("--auto-approve")
        {
            Description = "Comma-separated approval scopes to auto-approve: tool,file,shell,network,session,promptRead,all,none.",
            Recursive = true
        };

        AutoApproveBudgetOption = new Option<int?>("--auto-approve-budget")
        {
            Description = "Optional session budget for auto-approved elevated operations.",
            Recursive = true
        };

        YoloOption = new Option<bool>("--yolo")
        {
            Description = "Forces dangerFullAccess for one-shot/headless execution and suppresses approval prompts.",
            Recursive = true
        };
        YoloOption.Aliases.Add("-y");

        PrimaryModeOption = new Option<string>("--primary-mode")
        {
            Description = "Sets the primary workflow mode: build, plan, or spec.",
            DefaultValueFactory = _ => "build",
            Recursive = true
        };

        SessionOption = new Option<string?>("--session")
        {
            Description = "Targets a specific SharpClaw session id for prompts.",
            Recursive = true
        };

        AgentOption = new Option<string?>("--agent")
        {
            Description = "Selects the effective agent id for prompt execution.",
            Recursive = true
        };

        HostIdOption = new Option<string?>("--host-id")
        {
            Description = "Sets the embedded host identifier for tenant-aware state and diagnostics.",
            Recursive = true
        };

        TenantIdOption = new Option<string?>("--tenant-id")
        {
            Description = "Sets the tenant identifier for enterprise session, memory, and metering operations.",
            Recursive = true
        };

        StorageRootOption = new Option<string?>("--storage-root")
        {
            Description = "Overrides the external storage root for embedded-host durable state.",
            Recursive = true
        };

        SessionStoreOption = new Option<string?>("--session-store")
        {
            Description = "Selects the embedded session store backend: fileSystem or sqlite.",
            Recursive = true
        };
    }

    /// <summary>
    /// Gets the output format option.
    /// </summary>
    public Option<string> OutputFormatOption { get; }

    /// <summary>
    /// Gets the working directory option.
    /// </summary>
    public Option<string?> WorkingDirectoryOption { get; }

    /// <summary>
    /// Gets the model option.
    /// </summary>
    public Option<string?> ModelOption { get; }

    /// <summary>
    /// Gets the permission mode option.
    /// </summary>
    public Option<string> PermissionModeOption { get; }

    /// <summary>
    /// Gets the auto-approve scopes option.
    /// </summary>
    public Option<string?> AutoApproveOption { get; }

    /// <summary>
    /// Gets the auto-approve budget option.
    /// </summary>
    public Option<int?> AutoApproveBudgetOption { get; }

    /// <summary>
    /// Gets the primary workflow mode option.
    /// </summary>
    public Option<string> PrimaryModeOption { get; }

    /// <summary>
    /// Gets the yolo option.
    /// </summary>
    public Option<bool> YoloOption { get; }

    /// <summary>
    /// Gets the optional session id option.
    /// </summary>
    public Option<string?> SessionOption { get; }

    /// <summary>
    /// Gets the optional agent id option.
    /// </summary>
    public Option<string?> AgentOption { get; }

    /// <summary>
    /// Gets the optional embedded host id option.
    /// </summary>
    public Option<string?> HostIdOption { get; }

    /// <summary>
    /// Gets the optional tenant id option.
    /// </summary>
    public Option<string?> TenantIdOption { get; }

    /// <summary>
    /// Gets the optional external storage root option.
    /// </summary>
    public Option<string?> StorageRootOption { get; }

    /// <summary>
    /// Gets the optional embedded session store kind option.
    /// </summary>
    public Option<string?> SessionStoreOption { get; }

    /// <summary>
    /// Gets all global options.
    /// </summary>
    public IEnumerable<Option> All =>
    [
        OutputFormatOption,
        WorkingDirectoryOption,
        ModelOption,
        PermissionModeOption,
        AutoApproveOption,
        AutoApproveBudgetOption,
        YoloOption,
        PrimaryModeOption,
        SessionOption,
        AgentOption,
        HostIdOption,
        TenantIdOption,
        StorageRootOption,
        SessionStoreOption,
    ];

    /// <summary>
    /// Resolves a command execution context from a parse result.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <returns>The normalized command execution context.</returns>
    public CommandExecutionContext Resolve(ParseResult parseResult)
    {
        var outputFormatText = parseResult.GetValue(OutputFormatOption) ?? "text";
        var permissionModeText = parseResult.GetValue(PermissionModeOption) ?? "workspaceWrite";
        var autoApproveText = parseResult.GetValue(AutoApproveOption);
        var autoApproveBudget = parseResult.GetValue(AutoApproveBudgetOption);
        var yolo = parseResult.GetValue(YoloOption);
        var cwd = parseResult.GetValue(WorkingDirectoryOption);
        var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(cwd)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(cwd);
        var primaryText = parseResult.GetValue(PrimaryModeOption) ?? "build";
        var hostId = parseResult.GetValue(HostIdOption);
        var tenantId = parseResult.GetValue(TenantIdOption);
        var storageRoot = parseResult.GetValue(StorageRootOption);
        var sessionStoreText = parseResult.GetValue(SessionStoreOption);
        var hostContext = CreateHostContext(hostId, tenantId, storageRoot, sessionStoreText);
        var approvalSettings = ApprovalSettingsText.Parse(autoApproveText, autoApproveBudget);

        return new CommandExecutionContext(
            WorkingDirectory: resolvedWorkingDirectory,
            Model: parseResult.GetValue(ModelOption),
            PermissionMode: yolo ? PermissionMode.DangerFullAccess : ParsePermissionMode(permissionModeText),
            OutputFormat: ParseOutputFormat(outputFormatText),
            PrimaryMode: ParsePrimaryMode(primaryText),
            SessionId: parseResult.GetValue(SessionOption),
            AgentId: parseResult.GetValue(AgentOption),
            HostContext: hostContext,
            ApprovalSettings: approvalSettings);
    }

    private static RuntimeHostContext? CreateHostContext(
        string? hostId,
        string? tenantId,
        string? storageRoot,
        string? sessionStoreText)
    {
        var normalizedHostId = string.IsNullOrWhiteSpace(hostId) ? null : hostId.Trim();
        var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
        var normalizedStorageRoot = string.IsNullOrWhiteSpace(storageRoot) ? null : Path.GetFullPath(storageRoot);
        var sessionStoreKind = ParseSessionStoreKind(sessionStoreText);

        if (normalizedHostId is null
            && normalizedTenantId is null
            && normalizedStorageRoot is null
            && sessionStoreKind is null)
        {
            return null;
        }

        return new RuntimeHostContext(
            HostId: normalizedHostId ?? "sharpclaw-cli",
            TenantId: normalizedTenantId,
            StorageRoot: normalizedStorageRoot,
            SessionStoreKind: sessionStoreKind ?? SessionStoreKind.FileSystem,
            IsEmbeddedHost: true);
    }

    private static OutputFormat ParseOutputFormat(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            _ => OutputFormat.Text,
        };

    private static PermissionMode ParsePermissionMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read-only" => PermissionMode.ReadOnly,
            "workspacewrite" or "workspace-write" or "prompt" or "autoapprovesafe" or "auto-approve-safe" => PermissionMode.WorkspaceWrite,
            "dangerfullaccess" or "danger-full-access" or "fulltrust" or "full-trust" => PermissionMode.DangerFullAccess,
            _ => PermissionMode.WorkspaceWrite,
        };

    private static PrimaryMode ParsePrimaryMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "plan" => PrimaryMode.Plan,
            "spec" => PrimaryMode.Spec,
            _ => PrimaryMode.Build,
        };

    private static SessionStoreKind? ParseSessionStoreKind(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "filesystem" or "file-system" or "fileSystem" => SessionStoreKind.FileSystem,
            "sqlite" => SessionStoreKind.Sqlite,
            _ => SessionStoreKind.FileSystem,
        };
}
