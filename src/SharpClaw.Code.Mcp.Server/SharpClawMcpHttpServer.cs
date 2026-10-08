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
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(SharpClawMcpServer).Assembly.GetName().Name });
        builder.WebHost.UseUrls(endpoint.AbsoluteUri.TrimEnd('/'));
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
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
            if (!AcceptHost(context.Request.Host, endpoint))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var origin = context.Request.Headers.Origin;
            if (origin.Count > 0 && (origin.Count != 1 || !Uri.TryCreate(origin[0], UriKind.Absolute, out var uri)
                || uri.Scheme != endpoint.Scheme || uri.Authority != context.Request.Host.Value
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

    /// <summary>Validates explicit HTTP exposure; elevated and remote hosts require a secret.</summary>
    public static Uri ValidateHttpOptions(SharpClawMcpServerOptions options)
    {
        if (options.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(options), "HTTP port must be 1..65535.");
        var localhost = options.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!localhost && (!IPAddress.TryParse(options.Host, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            throw new ArgumentException("HTTP host must be localhost or an explicit non-wildcard IP address.", nameof(options));
        var loopback = localhost || IPAddress.IsLoopback(IPAddress.Parse(options.Host));
        if (!loopback && !options.AllowRemote) throw new ArgumentException("Non-loopback HTTP requires AllowRemote.", nameof(options));
        if ((!loopback || options.PermissionMode != PermissionMode.ReadOnly) && string.IsNullOrWhiteSpace(options.BearerToken))
            throw new ArgumentException("Remote or elevated HTTP requires SHARPCLAW_MCP_TOKEN or a host-supplied BearerToken.", nameof(options));
        if (options.BearerToken is not null && (options.BearerToken.Length < 16 || options.BearerToken.Any(char.IsWhiteSpace)))
            throw new ArgumentException("HTTP bearer token must contain at least 16 characters without whitespace.", nameof(options));
        return new UriBuilder("http", options.Host, options.Port).Uri;
    }

    private static bool AcceptHost(HostString host, Uri endpoint)
    {
        if (host.Port != endpoint.Port) return false;
        var name = host.Host.Trim('[', ']');
        if (endpoint.IsLoopback) return name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address);
        return name.Equals(endpoint.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasToken(HttpRequest request, string expected)
    {
        var header = request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value[7..])), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
