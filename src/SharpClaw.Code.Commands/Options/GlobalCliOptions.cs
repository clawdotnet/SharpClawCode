using System.CommandLine;
using System.CommandLine.Parsing;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Enums;

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
    /// Gets the primary workflow mode option.
    /// </summary>
    public Option<string> PrimaryModeOption { get; }

    /// <summary>
    /// Gets the optional session id option.
    /// </summary>
    public Option<string?> SessionOption { get; }

    /// <summary>
    /// Gets all global options.
    /// </summary>
    public IEnumerable<Option> All => [OutputFormatOption, WorkingDirectoryOption, ModelOption, PermissionModeOption, PrimaryModeOption, SessionOption];

    /// <summary>
    /// Resolves a command execution context from a parse result.
    /// </summary>
    /// <param name="parseResult">The parse result.</param>
    /// <returns>The normalized command execution context.</returns>
    public CommandExecutionContext Resolve(ParseResult parseResult)
    {
        var outputFormatText = parseResult.GetValue(OutputFormatOption) ?? "text";
        var permissionModeText = parseResult.GetValue(PermissionModeOption) ?? "workspaceWrite";
        var cwd = parseResult.GetValue(WorkingDirectoryOption);
        var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(cwd)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(cwd);

        var primaryText = parseResult.GetValue(PrimaryModeOption) ?? "build";
        return new CommandExecutionContext(
            WorkingDirectory: resolvedWorkingDirectory,
            Model: parseResult.GetValue(ModelOption),
            PermissionMode: ParsePermissionMode(permissionModeText),
            OutputFormat: ParseOutputFormat(outputFormatText),
            PrimaryMode: ParsePrimaryMode(primaryText),
            SessionId: parseResult.GetValue(SessionOption));
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
}
