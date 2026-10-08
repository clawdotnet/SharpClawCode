using System.Text.Json;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using SharpClaw.Code.Cli;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Mcp;

/// <summary>Exercises the inbound MCP transport with real SDK clients.</summary>
public sealed class InboundMcpStdioTests
{
    /// <summary>Verifies permissions, protocol contracts, and transport lifecycle.</summary>
    [Fact]
    public async Task Default_server_negotiates_exposes_curated_tools_and_denies_evaluation_execution_and_hidden_rename()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = await StartAsync(fixture.Root, [], timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        tools.Should().HaveCount(8);
        tools.Should().NotContain(t => t.Name == "csharp_rename_symbol");
        tools.Single(t => t.Name == "workspace_search").JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString().Should().Be("string");
        var search = await client.CallToolAsync("workspace_search", new Dictionary<string, object?> { ["query"] = "Invoice" }, cancellationToken: timeout.Token);
        search.IsError.Should().NotBeTrue();
        search.StructuredContent.Should().NotBeNull();
        var inspect = await client.CallToolAsync("dotnet_solution_inspect", cancellationToken: timeout.Token);
        inspect.IsError.Should().BeTrue();
        inspect.StructuredContent!.Value.GetProperty("status").GetString().Should().Be("PermissionDenied");
        File.Exists(Path.Combine(fixture.Root, "Library", "evaluation-sentinel.txt")).Should().BeFalse();
        var verify = await client.CallToolAsync("verify_workspace", cancellationToken: timeout.Token);
        verify.IsError.Should().BeTrue();
        verify.StructuredContent!.Value.GetProperty("reason").GetString().Should().Be("PermissionDenied");
        var rename = await client.CallToolAsync("csharp_rename_symbol", new Dictionary<string, object?> { ["name"] = "IInvoiceService", ["newName"] = "IBillingService" }, cancellationToken: timeout.Token);
        rename.IsError.Should().BeTrue();
    }

    /// <summary>Verifies permissions, protocol contracts, and transport lifecycle.</summary>
    [Fact]
    public async Task Explicit_elevation_allows_semantics_and_opted_in_rename_records_an_undoable_session()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using (var client = await StartAsync(fixture.Root, ["--permission-mode", "dangerFullAccess", "--allow-mutations"], timeout.Token))
        {
            (await client.ListToolsAsync(cancellationToken: timeout.Token)).Should().HaveCount(9);
            var inspect = await client.CallToolAsync("dotnet_solution_inspect", cancellationToken: timeout.Token);
            inspect.IsError.Should().NotBeTrue();
            inspect.StructuredContent!.Value.GetProperty("projects").GetArrayLength().Should().Be(2);
            var rename = await client.CallToolAsync("csharp_rename_symbol", new Dictionary<string, object?> { ["name"] = "IInvoiceService", ["newName"] = "IBillingService" }, cancellationToken: timeout.Token);
            rename.IsError.Should().NotBeTrue(rename.Content.ToString());
        }
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSharpClawRuntime(builder.Configuration);
        using var host = builder.Build();
        var session = (await host.Services.GetRequiredService<ISessionStore>().GetLatestAsync(fixture.Root, timeout.Token))!;
        CheckpointMutationCoordinator.ToSnapshot(session).TotalMutationSets.Should().Be(1);
        var undo = await host.Services.GetRequiredService<CheckpointMutationCoordinator>().TryUndoAsync(fixture.Root, session.Id, timeout.Token);
        undo.Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Library", "Invoice.cs"), timeout.Token)).Should().Contain("IInvoiceService").And.NotContain("IBillingService");
    }

    /// <summary>The launched CLI performs real build and test verification on an explicitly restored fixture.</summary>
    [Fact]
    public async Task Elevated_server_verifies_real_build_and_test_results()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = await StartAsync(fixture.Root, ["--permission-mode", "dangerFullAccess"], timeout.Token);
        var result = await client.CallToolAsync("verify_workspace", new Dictionary<string, object?> { ["scope"] = "all" }, cancellationToken: timeout.Token);
        result.IsError.Should().NotBeTrue(result.Content.ToString());
        result.StructuredContent!.Value.GetProperty("status").GetString().Should().Be("Passed");
        result.StructuredContent.Value.GetProperty("tests").GetProperty("passed").GetInt32().Should().Be(1);
        (await client.ListToolsAsync(cancellationToken: timeout.Token)).Should().NotContain(t => t.Name == "csharp_rename_symbol");
    }

    /// <summary>Explicit workflow modes retain their execution and mutation restrictions.</summary>
    [Theory]
    [InlineData("plan")]
    [InlineData("research")]
    public async Task Workflow_mode_restrictions_are_forwarded_to_inbound_permissions(string mode)
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await StartAsync(fixture.Root, ["--permission-mode", "dangerFullAccess", "--allow-mutations", "--primary-mode", mode], timeout.Token);
        (await client.ListToolsAsync(cancellationToken: timeout.Token)).Should().NotContain(t => t.Name == "csharp_rename_symbol");
        (await client.CallToolAsync("dotnet_solution_inspect", cancellationToken: timeout.Token)).IsError.Should().BeTrue();
        (await client.CallToolAsync("verify_workspace", cancellationToken: timeout.Token)).IsError.Should().BeTrue();
    }

    /// <summary>Every stdout line is JSON-RPC, and closing stdin cleanly stops the native or framework-dependent host.</summary>
    [Fact]
    public async Task Stdout_contains_only_protocol_messages_and_eof_stops_server()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var binary = Environment.GetEnvironmentVariable("SHARPCLAW_TEST_BINARY");
        var start = new ProcessStartInfo(binary ?? "dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        if (binary is null) { start.ArgumentList.Add("exec"); start.ArgumentList.Add(typeof(CliHostBuilder).Assembly.Location); }
        foreach (var arg in new[] { "mcp", "serve", "--cwd", fixture.Root }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"wire-test","version":"1"}}}""");
            await ResponseAsync(1);
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            await ResponseAsync(2);
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"workspace_search","arguments":{"query":"Invoice"}}}""");
            await ResponseAsync(3);
            process.StandardInput.Close();
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line) { using var remaining = Validate(line); }
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().Be(0, await stderr);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }

        async Task ResponseAsync(int id)
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var document = Validate(line);
                if (document.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id)
                { document.RootElement.TryGetProperty("error", out _).Should().BeFalse(line); return; }
            }
            throw new InvalidOperationException("MCP stopped before replying: " + await stderr);
        }
        static JsonDocument Validate(string line)
        {
            var document = JsonDocument.Parse(line);
            document.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
            return document;
        }
    }

    internal static Task<McpClient> StartAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var dll = typeof(CliHostBuilder).Assembly.Location;
        var binary = Environment.GetEnvironmentVariable("SHARPCLAW_TEST_BINARY");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = binary ?? "dotnet", Arguments = binary is null ? ["exec", dll, "mcp", "serve", "--cwd", root, .. arguments] : ["mcp", "serve", "--cwd", root, .. arguments],
            WorkingDirectory = root, Name = "sharpclaw-test",
        });
        return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }
}
