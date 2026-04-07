using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Cli.Composition;
using SharpClaw.Code.Runtime;

namespace SharpClaw.Code.Cli;

/// <summary>
/// Creates the shared host used by the SharpClaw Code CLI entry point.
/// </summary>
public static class CliHostBuilder
{
    /// <summary>
    /// Builds the application host for the CLI vertical slice.
    /// </summary>
    /// <param name="args">Optional startup arguments.</param>
    /// <returns>A configured host instance.</returns>
    public static IHost BuildHost(string[]? args = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Logging.ClearProviders();
        builder.Services.AddSharpClawRuntime(builder.Configuration);
        builder.Services.AddSharpClawCli();
        return builder.Build();
    }
}
