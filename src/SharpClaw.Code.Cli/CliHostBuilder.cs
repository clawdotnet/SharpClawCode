using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Runtime.Workflow;
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
        // Repository appsettings files are untrusted and must never configure credential destinations.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            Args = args ?? [],
        });
        builder.Configuration.AddEnvironmentVariables();
        builder.Logging.ClearProviders();
        builder.Services.AddSharpClawRuntime(builder.Configuration);
        foreach (var descriptor in builder.Services.Where(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(ScheduledPromptRunner)).ToArray())
            builder.Services.Remove(descriptor);
        builder.Services.AddSharpClawCli();
        return builder.Build();
    }
}
