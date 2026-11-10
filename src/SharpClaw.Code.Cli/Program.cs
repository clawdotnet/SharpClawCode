using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using System.CommandLine;

using var host = CliHostBuilder.BuildHost(args);
var renderer = host.Services.GetRequiredService<OutputRendererDispatcher>();
var format = args.Where((value, index) => index > 0 && args[index - 1] == "--output-format").LastOrDefault() == "json"
    || args.Contains("--output-format=json") ? OutputFormat.Json : OutputFormat.Text;
try
{
    var rootCommand = await host.Services.GetRequiredService<CliCommandFactory>().CreateRootCommandAsync();
    var parsed = rootCommand.Parse(args);
    if (parsed.Errors.Count > 0)
        throw new ArgumentException(string.Join(Environment.NewLine, parsed.Errors.Select(error => error.Message)));
    // Commands own their runtime lifetime. ConsoleLifetime would cancel the REPL itself on Ctrl+C.
    return await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (OperationCanceledException)
{
    await renderer.RenderCommandResultAsync(new CommandResult(false, 130, format, "Operation cancelled.", null), format, CancellationToken.None);
    return 130;
}
catch (Exception exception)
{
    await renderer.RenderCommandResultAsync(new CommandResult(false, 1, format, exception.Message, null), format, CancellationToken.None);
    return 1;
}
