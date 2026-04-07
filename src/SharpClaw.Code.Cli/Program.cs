using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;
using System.CommandLine;

using var host = CliHostBuilder.BuildHost(args);
await host.StartAsync();

try
{
    var commandFactory = host.Services.GetRequiredService<CliCommandFactory>();
    var rootCommand = await commandFactory.CreateRootCommandAsync();
    return await rootCommand.Parse(args).InvokeAsync();
}
finally
{
    await host.StopAsync();
}
