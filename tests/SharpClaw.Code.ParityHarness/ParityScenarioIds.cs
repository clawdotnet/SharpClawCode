namespace SharpClaw.Code.ParityHarness;

/// <summary>
/// Stable scenario ids for filters, docs, and CI (<c>dotnet test --filter FullyQualifiedName~streaming_text</c> uses test names;
/// these ids are the contract names for parity coverage).
/// </summary>
internal static class ParityScenarioIds
{
    public const string VerificationBuildSuccess = "verification_build_success";
    public const string VerificationBuildFailure = "verification_build_failure";
    public const string VerificationPermissionDenied = "verification_permission_denied";
    public const string SemanticRenameMutationRecorded = "semantic_rename_mutation_recorded";
    public const string SemanticSymbolResolution = "semantic_symbol_resolution";
    public const string StreamingText = "streaming_text";
    public const string ReadFileRoundtrip = "read_file_roundtrip";
    public const string WriteFileAllowed = "write_file_allowed";
    public const string WriteFileDenied = "write_file_denied";
    public const string GrepChunkAssembly = "grep_chunk_assembly";
    public const string BashStdoutRoundtrip = "bash_stdout_roundtrip";
    public const string PermissionPromptApproved = "permission_prompt_approved";
    public const string PermissionPromptDenied = "permission_prompt_denied";
    public const string PluginToolRoundtrip = "plugin_tool_roundtrip";
    public const string McpPartialStartup = "mcp_partial_startup";
    public const string RecoveryAfterTimeout = "recovery_after_timeout";
    public const string ToolCallRoundtrip = "tool_call_roundtrip";

    /// <summary>
    /// All first-class parity scenarios expected in this harness.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        VerificationBuildSuccess,
        VerificationBuildFailure,
        VerificationPermissionDenied,
        SemanticSymbolResolution,
        SemanticRenameMutationRecorded,
        StreamingText,
        ReadFileRoundtrip,
        WriteFileAllowed,
        WriteFileDenied,
        GrepChunkAssembly,
        BashStdoutRoundtrip,
        PermissionPromptApproved,
        PermissionPromptDenied,
        PluginToolRoundtrip,
        McpPartialStartup,
        RecoveryAfterTimeout,
        ToolCallRoundtrip,
    ];
}
