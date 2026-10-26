using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Permissions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Registry;

namespace McpToolAgent;

/// <summary>
/// Publishes a local folder as a public static site through the keyless deed.page MCP server,
/// gated by SharpClaw's network-access approval.
/// </summary>
public static class DeedPageExample
{
    /// <summary>The keyless Streamable HTTP MCP endpoint.</summary>
    public static readonly Uri Endpoint = new("https://deed.page/mcp");

    /// <summary>Identifies this example on discovery and tool requests.</summary>
    public const string UserAgent = "SharpClaw.Code-McpToolAgent/0.1.0";

    /// <summary>The local tool name registered in the SharpClaw tool registry.</summary>
    public const string ToolName = "deed_page_publish_site";

    /// <summary>deed.page's per-request body cap (4 MB), in bytes.</summary>
    public const long MaxRequestBytes = 4 * 1024 * 1024;

    /// <summary>Room left in each request for the MCP JSON-RPC envelope around the arguments.</summary>
    public const long RequestEnvelopeReserveBytes = 16 * 1024;

    /// <summary>The largest serialized publish_site arguments this example sends, in bytes.</summary>
    public const long MaxArgumentsBytes = MaxRequestBytes - RequestEnvelopeReserveBytes;

    /// <summary>
    /// Builds publish_site arguments from the files under <paramref name="directory"/>.
    /// Hidden files and folders and symbolic links are skipped; UTF-8 text is sent as-is and
    /// other files as base64. Folders whose encoded arguments would not fit in one deed.page
    /// request are rejected before any request is made.
    /// </summary>
    /// <param name="directory">The folder to publish. It should contain an index.html.</param>
    /// <param name="ttlSeconds">Optional expiry in seconds; null publishes a permanent site.</param>
    /// <param name="cancellationToken">Cancels file reads.</param>
    /// <returns>The JSON arguments for the remote publish_site tool.</returns>
    public static async Task<string> CreatePublishArgumentsAsync(
        string directory,
        int? ttlSeconds,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder '{directory}' was not found.");
        }

        var tooLarge = $"Folder '{directory}' does not fit in one deed.page publish request "
            + $"({MaxRequestBytes} bytes, including base64 and JSON encoding). Publish a smaller folder, "
            + "or use deed.page's manifest upload (plan_upload and finalize_upload) for larger sites.";
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var files = new SortedDictionary<string, object>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", enumeration))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').Any(segment => segment.StartsWith('.')))
            {
                continue;
            }

            // Raw bytes never shrink when encoded, so this rejects oversized files before reading them.
            var remaining = MaxArgumentsBytes - totalBytes;
            if (new FileInfo(path).Length > remaining)
            {
                throw new InvalidOperationException(tooLarge);
            }

            if (!IsInsideRootWithoutLinks(root, Path.GetFullPath(path)))
            {
                throw new InvalidOperationException($"'{relative}' is not a regular file inside '{directory}'.");
            }

            var bytes = await ReadBoundedAsync(path, remaining, tooLarge, cancellationToken);
            totalBytes += bytes.LongLength;
            try
            {
                files[relative] = strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                files[relative] = new Dictionary<string, string> { ["base64"] = Convert.ToBase64String(bytes) };
            }
        }

        if (!files.ContainsKey("index.html"))
        {
            throw new InvalidOperationException($"Folder '{directory}' has no index.html.");
        }

        var arguments = new Dictionary<string, object> { ["files"] = files };
        if (ttlSeconds is > 0)
        {
            arguments["ttl_seconds"] = ttlSeconds.Value;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (json.LongLength > MaxArgumentsBytes)
        {
            throw new InvalidOperationException(tooLarge);
        }

        return Encoding.UTF8.GetString(json);
    }

    private static bool IsInsideRootWithoutLinks(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            return false;
        }

        // Re-check the file and every folder below the root just before opening, so a link
        // swapped in after enumeration is not followed out of the selected folder.
        for (var current = fullPath; !string.Equals(current, root, comparison); current = Path.GetDirectoryName(current))
        {
            if (current is null || File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        long maxBytes,
        string tooLargeMessage,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous,
        });
        if (stream.Length > maxBytes)
        {
            throw new InvalidOperationException(tooLargeMessage);
        }

        var buffer = new byte[stream.Length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        if (await stream.ReadAsync(new byte[1], cancellationToken) != 0)
        {
            throw new InvalidOperationException(tooLargeMessage);
        }

        return buffer;
    }

    /// <summary>
    /// Discovers publish_site, registers it, and executes one publish through the permission engine.
    /// The tool requires network-access approval, so nothing is uploaded unless the caller approves.
    /// No credentials are loaded or sent.
    /// </summary>
    /// <param name="argumentsJson">Arguments matching the remote publish_site schema.</param>
    /// <param name="context">The caller's workspace, allowlist, and permission context.</param>
    /// <param name="cancellationToken">Cancels discovery and execution.</param>
    /// <param name="httpClient">Optional caller-owned HTTP client, primarily for transport testing.</param>
    /// <returns>The permission decision and tool result, including remote errors.</returns>
    public static async Task<ToolExecutionEnvelope> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext context,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
            AdditionalHeaders = new Dictionary<string, string> { ["User-Agent"] = UserAgent }
        };
        var transport = httpClient is null
            ? new HttpClientTransport(options)
            : new HttpClientTransport(options, httpClient, ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var discovered = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var remoteTool = discovered.Single(tool => tool.Name == "publish_site");
        var registry = new ToolRegistry([new DeedPagePublishTool(client, remoteTool)]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharpClawInfrastructure();
        services.AddSharpClawPermissions();
        using var provider = services.BuildServiceProvider();
        var executor = new ToolExecutor(registry, provider.GetRequiredService<IPermissionPolicyEngine>());
        return await executor.ExecuteAsync(ToolName, argumentsJson, context, cancellationToken);
    }

    private sealed class DeedPagePublishTool(McpClient client, McpClientTool remoteTool) : SharpClawToolBase
    {
        public override ToolDefinition Definition { get; } = new(
            ToolName,
            "Publish files as a public static website on deed.page. Anyone with the returned URL can view it.",
            ApprovalScope.NetworkAccess,
            IsDestructive: false,
            RequiresApproval: true,
            InputTypeName: "DeedPagePublishArguments",
            InputDescription: "Arguments follow the discovered publish_site input schema.",
            Tags: ["web", "network", "publish", "deed-page", "mcp"],
            InputSchemaJson: remoteTool.JsonSchema.GetRawText());

        public override async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(request.ArgumentsJson)
                ?? throw new ArgumentException("Tool arguments must be a JSON object.");
            var result = await client.CallToolAsync(remoteTool.Name, arguments, cancellationToken: cancellationToken);
            var output = string.Join(Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            if (result.IsError == true)
            {
                return CreateFailureResult(context, request, output);
            }

            return CreateSuccessResult(context, request, output, result);
        }
    }
}
