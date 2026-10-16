using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Mcp.Server;

public sealed partial class SharpClawMcpServer
{
    private async Task RunHttpAsync(SharpClawMcpServerOptions options, CancellationToken cancellationToken)
    {
        var endpoint = ValidateHttpOptions(options);
        // Workspace configuration and ASPNETCORE_/Kestrel__ environment settings must never select listeners.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(SharpClawMcpServer).Assembly.GetName().Name,
        });
        builder.WebHost.UseKestrel().ConfigureKestrel(kestrel =>
        {
            if (options.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) kestrel.ListenLocalhost(options.Port);
            else kestrel.Listen(IPAddress.Parse(options.Host), options.Port);
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddRouting();
        builder.Services.AddMcpServer(server => server.ServerInfo = new()
            { Name = "SharpClaw.Code", Version = typeof(SharpClawMcpServer).Assembly.GetName().Version?.ToString() ?? "1.0.0" })
            .WithHttpTransport(transport =>
            {
                transport.Stateless = false;
                transport.IdleTimeout = TimeSpan.FromMinutes(5);
                transport.MaxIdleSessionCount = 64;
            })
            .WithListToolsHandler((request, ct) => bridge.ListAsync(options, ct))
            .WithCallToolHandler((request, ct) => bridge.CallAsync(options, request, ct));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var accepted = AcceptedOrigin(context.Request.Host, endpoint, options.AllowedHosts);
            if (accepted is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var origin = context.Request.Headers.Origin;
            if (origin.Count > 0 && (origin.Count != 1 || !Uri.TryCreate(origin[0], UriKind.Absolute, out var uri)
                || uri.Scheme != accepted.Scheme || !uri.Host.Equals(accepted.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != accepted.Port
                || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (options.BearerToken is not null && !HasToken(context.Request, options.BearerToken))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next(context).ConfigureAwait(false);
        });
        app.MapMcp("/mcp");
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Allows loopback listeners only; authenticated remote access uses a local HTTPS reverse proxy.</summary>
    public static Uri ValidateHttpOptions(SharpClawMcpServerOptions options)
    {
        if (options.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(options), "HTTP port must be 1..65535.");
        var localhost = options.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!localhost && (!IPAddress.TryParse(options.Host, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            throw new ArgumentException("HTTP host must be localhost or an explicit non-wildcard IP address.", nameof(options));
        var loopback = localhost || IPAddress.IsLoopback(IPAddress.Parse(options.Host));
        if (!loopback) throw new ArgumentException("HTTP listeners must be loopback. Use a local TLS reverse proxy and AllowedHosts for remote access.", nameof(options));
        foreach (var host in options.AllowedHosts ?? [])
        {
            if (!Uri.TryCreate("https://" + host, UriKind.Absolute, out var proxy) || proxy.HostNameType != UriHostNameType.Dns
                || proxy.IsLoopback || proxy.AbsolutePath != "/" || proxy.Query.Length != 0 || proxy.Fragment.Length != 0 || proxy.UserInfo.Length != 0
                || proxy.Authority != host.ToLowerInvariant())
                throw new ArgumentException("AllowedHosts must contain HTTPS DNS authorities without paths or explicit default ports.", nameof(options));
        }
        if ((options.AllowedHosts is { Length: > 0 } || options.PermissionMode != PermissionMode.ReadOnly) && string.IsNullOrWhiteSpace(options.BearerToken))
            throw new ArgumentException("Proxied or elevated HTTP requires SHARPCLAW_MCP_TOKEN or a host-supplied BearerToken.", nameof(options));
        if (options.BearerToken is not null && (options.BearerToken.Length < 16 || options.BearerToken.Any(char.IsWhiteSpace)))
            throw new ArgumentException("HTTP bearer token must contain at least 16 characters without whitespace.", nameof(options));
        return new UriBuilder("http", options.Host, options.Port).Uri;
    }

    private static Uri? AcceptedOrigin(HostString host, Uri endpoint, string[]? allowedHosts)
    {
        var name = host.Host.Trim('[', ']');
        if ((host.Port ?? 80) == endpoint.Port && (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address)))
            return new UriBuilder("http", name, endpoint.Port).Uri;
        foreach (var allowed in allowedHosts ?? [])
        {
            var proxy = new Uri("https://" + allowed);
            if (name.Equals(proxy.Host, StringComparison.OrdinalIgnoreCase) && (host.Port ?? 443) == proxy.Port) return proxy;
        }
        return null;
    }

    private static bool HasToken(HttpRequest request, string expected)
    {
        var header = request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value[7..])), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
