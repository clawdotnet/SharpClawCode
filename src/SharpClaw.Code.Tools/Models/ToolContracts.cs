namespace SharpClaw.Code.Tools.Models;

using SharpClaw.Code.Web.Models;

/// <summary>
/// Arguments for reading a file from the workspace.
/// </summary>
/// <param name="Path">The file path, absolute or relative to the workspace root.</param>
/// <param name="Offset">Optional 1-based starting line offset. Negative values count back from the end.</param>
/// <param name="Limit">Optional maximum number of lines to return.</param>
public sealed record ReadFileToolArguments(string Path, int? Offset, int? Limit);

/// <summary>
/// Structured result for reading a file.
/// </summary>
/// <param name="Path">The normalized workspace-relative path.</param>
/// <param name="Exists">Indicates whether the file existed.</param>
/// <param name="Content">The formatted file content.</param>
/// <param name="StartLine">The first line number included in the response.</param>
/// <param name="EndLine">The last line number included in the response.</param>
/// <param name="TotalLineCount">The total line count for the file.</param>
public sealed record ReadFileToolResult(string Path, bool Exists, string Content, int StartLine, int EndLine, int TotalLineCount);

/// <summary>
/// Arguments for writing a file in the workspace.
/// </summary>
/// <param name="Path">The file path, absolute or relative to the workspace root.</param>
/// <param name="Content">The full file content to write.</param>
public sealed record WriteFileToolArguments(string Path, string Content);

/// <summary>
/// Arguments for editing a file by replacing one occurrence.
/// </summary>
/// <param name="Path">The file path, absolute or relative to the workspace root.</param>
/// <param name="OldString">The unique string to replace.</param>
/// <param name="NewString">The replacement content.</param>
public sealed record EditFileToolArguments(string Path, string OldString, string NewString);

/// <summary>
/// Structured result for write and edit file operations.
/// </summary>
/// <param name="Path">The normalized workspace-relative path.</param>
/// <param name="Message">A concise operation summary.</param>
public sealed record FileMutationToolResult(string Path, string Message);

/// <summary>
/// Arguments for globbing files in the workspace.
/// </summary>
/// <param name="Pattern">The glob pattern to match against workspace-relative file paths.</param>
/// <param name="Limit">The maximum number of paths to return.</param>
public sealed record GlobSearchToolArguments(string Pattern, int? Limit);

/// <summary>
/// Structured result for glob search.
/// </summary>
/// <param name="Pattern">The original pattern.</param>
/// <param name="Paths">The matched workspace-relative paths.</param>
public sealed record GlobSearchToolResult(string Pattern, string[] Paths);

/// <summary>
/// Arguments for searching file contents with a regular expression.
/// </summary>
/// <param name="Pattern">The regular expression to match.</param>
/// <param name="Glob">An optional glob filter for candidate files.</param>
/// <param name="Limit">The maximum number of matches to return.</param>
/// <param name="CaseSensitive">Indicates whether matching should be case-sensitive.</param>
public sealed record GrepSearchToolArguments(string Pattern, string? Glob, int? Limit, bool CaseSensitive);

/// <summary>
/// Represents a single grep match.
/// </summary>
/// <param name="Path">The workspace-relative file path.</param>
/// <param name="LineNumber">The 1-based line number.</param>
/// <param name="LineText">The matching line text.</param>
public sealed record GrepSearchMatch(string Path, int LineNumber, string LineText);

/// <summary>
/// Structured result for grep search.
/// </summary>
/// <param name="Pattern">The original pattern.</param>
/// <param name="Matches">The matching lines.</param>
public sealed record GrepSearchToolResult(string Pattern, GrepSearchMatch[] Matches);

/// <summary>
/// Arguments for running a shell command.
/// </summary>
/// <param name="Command">The shell command text.</param>
/// <param name="WorkingDirectory">An optional working directory relative to the workspace root.</param>
/// <param name="EnvironmentVariables">Optional environment variable overrides.</param>
public sealed record BashToolArguments(
    string Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables);

/// <summary>
/// Structured result for bash execution.
/// </summary>
/// <param name="WorkingDirectory">The effective working directory used for execution.</param>
/// <param name="ExitCode">The shell exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
public sealed record BashToolResult(string WorkingDirectory, int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Arguments for searching tool metadata.
/// </summary>
/// <param name="Query">The optional free-text query.</param>
/// <param name="Limit">The maximum number of metadata entries to return.</param>
public sealed record ToolSearchToolArguments(string? Query, int? Limit);

/// <summary>
/// Structured result for tool metadata search.
/// </summary>
/// <param name="Tools">The matching tool definitions.</param>
public sealed record ToolSearchToolResult(ToolDefinition[] Tools);

/// <summary>
/// Arguments for hybrid workspace search.
/// </summary>
/// <param name="Query">Search query.</param>
/// <param name="Limit">Maximum number of hits.</param>
/// <param name="IncludeSymbols">Whether symbol hits should be included.</param>
/// <param name="IncludeSemantic">Whether semantic hits should be included.</param>
public sealed record WorkspaceSearchToolArguments(string Query, int? Limit, bool IncludeSymbols = true, bool IncludeSemantic = true);

/// <summary>
/// Arguments for symbol-only workspace search.
/// </summary>
/// <param name="Query">Symbol query.</param>
/// <param name="Limit">Maximum number of hits.</param>
public sealed record SymbolSearchToolArguments(string Query, int? Limit);

/// <summary>
/// Arguments for performing a structured web search.
/// </summary>
/// <param name="Query">The search query to execute.</param>
/// <param name="Limit">The maximum number of results to return.</param>
public sealed record WebSearchToolArguments(string Query, int? Limit);

/// <summary>
/// Structured result for web search.
/// </summary>
/// <param name="Query">The executed query.</param>
/// <param name="Provider">The provider label.</param>
/// <param name="Results">The structured search results.</param>
public sealed record WebSearchToolResult(string Query, string Provider, WebSearchResult[] Results);

/// <summary>
/// Arguments for fetching a structured web document.
/// </summary>
/// <param name="Url">The URL to fetch.</param>
public sealed record WebFetchToolArguments(string Url);

/// <summary>
/// Structured result for fetching a web document.
/// </summary>
/// <param name="Url">The fetched URL.</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="ContentType">The response content type.</param>
/// <param name="Title">The normalized document title.</param>
/// <param name="Content">The normalized document content.</param>
public sealed record WebFetchToolResult(string Url, int StatusCode, string? ContentType, string? Title, string Content);
