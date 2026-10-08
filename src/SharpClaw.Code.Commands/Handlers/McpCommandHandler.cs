using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Provides the MCP management command surface.
/// </summary>
public sealed class McpCommandHandler(
    IMcpRegistry mcpRegistry,
    IMcpServerHost mcpServerHost,
    IMcpDoctorService mcpDoctorService,
    OutputRendererDispatcher outputRendererDispatcher,
    ISharpClawMcpServer? inboundServer = null) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "mcp";

    /// <inheritdoc />
    public string Description => "Inspects and manages registered MCP servers.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildServeCommand(globalOptions));
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildStatusCommand(globalOptions));
        command.Subcommands.Add(BuildRegisterCommand(globalOptions));
        command.Subcommands.Add(BuildStartCommand(globalOptions));
        command.Subcommands.Add(BuildStopCommand(globalOptions));
        command.Subcommands.Add(BuildRestartCommand(globalOptions));
        command.Subcommands.Add(BuildDoctorCommand(globalOptions));
        return command;
    }

    private Command BuildServeCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("serve", "Serves SharpClaw tools over stdio; defaults to readOnly without mutations.");
        var transport = new Option<string>("--transport") { DefaultValueFactory = _ => "stdio", Description = "stdio or http (Streamable HTTP)." };
        transport.Validators.Add(result => { if (result.GetValueOrDefault<string>() is not ("stdio" or "http")) result.AddError("Transport must be stdio or http."); });
        var host = new Option<string>("--host") { DefaultValueFactory = _ => "127.0.0.1" };
        var port = new Option<int>("--port") { DefaultValueFactory = _ => 7346 };
        var remote = new Option<bool>("--allow-remote") { Description = "Explicitly permit authenticated non-loopback HTTP." };
        command.Options.Add(transport); command.Options.Add(host); command.Options.Add(port); command.Options.Add(remote);
        var mutations = new Option<bool>("--allow-mutations") { Description = "Expose rename when the host permission mode permits writes." };
        command.Options.Add(mutations);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var explicitMode = parseResult.GetResult(globalOptions.PermissionModeOption) is { Implicit: false }
                || parseResult.GetValue(globalOptions.YoloOption);
            if (inboundServer is null) throw new InvalidOperationException("Register AddSharpClawMcpServer to host inbound MCP.");
            await inboundServer.RunAsync(new SharpClaw.Code.Mcp.Models.SharpClawMcpServerOptions(
                context.WorkingDirectory,
                explicitMode ? context.PermissionMode : PermissionMode.ReadOnly,
                parseResult.GetValue(mutations), context.ApprovalSettings, context.HostContext,
                parseResult.GetValue(transport) == "http" ? SharpClaw.Code.Mcp.Models.SharpClawMcpTransport.Http : SharpClaw.Code.Mcp.Models.SharpClawMcpTransport.Stdio,
                parseResult.GetValue(host)!, parseResult.GetValue(port), parseResult.GetValue(remote),
                Environment.GetEnvironmentVariable("SHARPCLAW_MCP_TOKEN"), context.PrimaryMode), cancellationToken).ConfigureAwait(false);
            return 0;
        });
        return command;
    }

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists registered MCP servers.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await mcpDoctorService.GetStatusAsync(context.WorkingDirectory, null, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });

        return command;
    }

    private Command BuildStatusCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("status", "Shows detailed status for a registered MCP server.");
        var idOption = new Option<string>("--id")
        {
            Required = true,
            Description = "The MCP server identifier."
        };

        command.Options.Add(idOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var serverId = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("The --id option is required.");
            var result = await mcpDoctorService.GetStatusAsync(context.WorkingDirectory, serverId, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });

        return command;
    }

    private Command BuildRegisterCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("register", "Registers or updates an MCP server definition.");
        var idOption = new Option<string>("--id") { Required = true, Description = "The stable MCP server identifier." };
        var nameOption = new Option<string>("--name") { Required = true, Description = "The display name for the server." };
        var endpointOption = new Option<string>("--command") { Required = true, Description = "The process command or endpoint to start." };
        var transportOption = new Option<string>("--transport")
        {
            Description = "Transport: stdio (default), http, https, streamable-http, or sse. Use an absolute URL in --command for HTTP/SSE.",
        };
        var argumentOption = new Option<string[]>("--arg") { Description = "Repeatable process arguments." };
        var enabledOption = new Option<bool>("--enabled") { Description = "Whether the server should be enabled by default." };

        command.Options.Add(idOption);
        command.Options.Add(nameOption);
        command.Options.Add(endpointOption);
        command.Options.Add(transportOption);
        command.Options.Add(argumentOption);
        command.Options.Add(enabledOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var id = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("The --id option is required.");
            var name = parseResult.GetValue(nameOption) ?? throw new InvalidOperationException("The --name option is required.");
            var endpoint = parseResult.GetValue(endpointOption) ?? throw new InvalidOperationException("The --command option is required.");
            var server = await mcpRegistry.RegisterAsync(
                context.WorkingDirectory,
                new McpServerDefinition(
                    Id: id,
                    DisplayName: name,
                    TransportKind: parseResult.GetValue(transportOption) ?? "stdio",
                    Endpoint: endpoint,
                    EnabledByDefault: parseResult.GetValue(enabledOption),
                    Environment: null,
                    Arguments: parseResult.GetValue(argumentOption)),
                cancellationToken).ConfigureAwait(false);

            var result = new CommandResult(
                Succeeded: true,
                ExitCode: 0,
                OutputFormat: context.OutputFormat,
                Message: $"Registered MCP server '{server.Definition.Id}' ({server.Status.State}).",
                DataJson: JsonSerializer.Serialize(server, ProtocolJsonContext.Default.RegisteredMcpServer));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private Command BuildStartCommand(GlobalCliOptions globalOptions)
        => BuildLifecycleCommand("start", "Starts a registered MCP server.", globalOptions, mcpServerHost.StartAsync);

    private Command BuildStopCommand(GlobalCliOptions globalOptions)
        => BuildLifecycleCommand("stop", "Stops a registered MCP server.", globalOptions, mcpServerHost.StopAsync);

    private Command BuildRestartCommand(GlobalCliOptions globalOptions)
        => BuildLifecycleCommand("restart", "Restarts a registered MCP server.", globalOptions, mcpServerHost.RestartAsync);

    private Command BuildDoctorCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("doctor", "Runs basic MCP diagnostics.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await mcpDoctorService.RunDoctorAsync(context.WorkingDirectory, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });

        return command;
    }

    private Command BuildLifecycleCommand(
        string name,
        string description,
        GlobalCliOptions globalOptions,
        Func<string, string, CancellationToken, Task<McpServerStatus>> action)
    {
        var command = new Command(name, description);
        var idOption = new Option<string>("--id")
        {
            Required = true,
            Description = "The MCP server identifier."
        };

        command.Options.Add(idOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var serverId = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("The --id option is required.");
            var status = await action(context.WorkingDirectory, serverId, cancellationToken).ConfigureAwait(false);
            var result = new CommandResult(
                Succeeded: status.State is not McpLifecycleState.Faulted,
                ExitCode: status.State is McpLifecycleState.Faulted ? 1 : 0,
                OutputFormat: context.OutputFormat,
                Message: $"{status.ServerId}: {status.State}{(string.IsNullOrWhiteSpace(status.StatusMessage) ? string.Empty : $" - {status.StatusMessage}")}",
                DataJson: JsonSerializer.Serialize(status, ProtocolJsonContext.Default.McpServerStatus));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });

        return command;
    }
}
