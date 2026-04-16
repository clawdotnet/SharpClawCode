using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Runtime;

namespace SharpClaw.Code;

/// <summary>
/// Builds an embeddable SharpClaw host without pulling in the CLI command surface.
/// </summary>
public sealed class SharpClawRuntimeHostBuilder
{
    private readonly HostApplicationBuilder builder;
    private bool runtimeRegistered;
    private bool acpRegistered;

    /// <summary>
    /// Initializes a new builder for an embeddable SharpClaw runtime host.
    /// </summary>
    public SharpClawRuntimeHostBuilder(string[]? args = null)
    {
        builder = Host.CreateApplicationBuilder(args ?? []);
    }

    /// <summary>
    /// Gets the underlying configuration root for additional host customization.
    /// </summary>
    public IConfigurationManager Configuration => builder.Configuration;

    /// <summary>
    /// Gets the service collection for additional registrations.
    /// </summary>
    public IServiceCollection Services => builder.Services;

    /// <summary>
    /// Configures the runtime services with configuration-backed providers.
    /// </summary>
    public SharpClawRuntimeHostBuilder AddRuntime()
    {
        if (!runtimeRegistered)
        {
            builder.Services.AddSharpClawRuntime(builder.Configuration);
            runtimeRegistered = true;
        }

        return this;
    }

    /// <summary>
    /// Adds ACP hosting support for editor subprocess integrations.
    /// </summary>
    public SharpClawRuntimeHostBuilder AddAcp()
    {
        if (!acpRegistered)
        {
            builder.Services.AddSharpClawAcp();
            acpRegistered = true;
        }

        return this;
    }

    /// <summary>
    /// Applies additional host customization before build.
    /// </summary>
    public SharpClawRuntimeHostBuilder Configure(Action<HostApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder);
        return this;
    }

    /// <summary>
    /// Builds the embeddable host wrapper.
    /// </summary>
    public SharpClawRuntimeHost Build()
    {
        AddRuntime();
        AddAcp();
        return new SharpClawRuntimeHost(builder.Build());
    }
}
