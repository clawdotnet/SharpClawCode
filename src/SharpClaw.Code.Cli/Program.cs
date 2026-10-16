using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;
using System.CommandLine;

using var host = CliHostBuilder.BuildHost(args);
var commandFactory = host.Services.GetRequiredService<CliCommandFactory>();
var rootCommand = await commandFactory.CreateRootCommandAsync();
var parsed = rootCommand.Parse(args);
// MCP owns its transport lifetime. Starting Runtime would also start schedulers and outbound clients.
var isMcpServer = parsed.CommandResult.Command.Name == "serve"
    && parsed.CommandResult.Parent is System.CommandLine.Parsing.CommandResult parent
    && parent.Command.Name == "mcp";
if (!isMcpServer) await host.StartAsync();
try { return await parsed.InvokeAsync(); }
finally { if (!isMcpServer) await host.StopAsync(); }
