using McpToolAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Composition;
using SharpClaw.Code.Tools.Abstractions;

if (args.Length > 0 && args[0] == "--parallel-search")
{
    if (args.Length != 3 || args[1] is not ("search" or "fetch"))
    {
        Console.Error.WriteLine("Usage: --parallel-search search <query> | --parallel-search fetch <url>");
        Environment.ExitCode = 2;
        return;
    }

    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    var conversationId = Guid.NewGuid().ToString("N");
    var toolName = args[1] == "search" ? "parallel_web_search" : "parallel_web_fetch";
    var arguments = args[1] == "search"
        ? System.Text.Json.JsonSerializer.Serialize(new
        {
            objective = args[2], search_queries = new[] { args[2] }, session_id = conversationId
        })
        : System.Text.Json.JsonSerializer.Serialize(new
        {
            urls = new[] { args[2] }, session_id = conversationId
        });
    var workspace = Directory.GetCurrentDirectory();
    var context = new SharpClaw.Code.Tools.Models.ToolExecutionContext(
        conversationId, "parallel-example", workspace, workspace,
        PermissionMode.ReadOnly, OutputFormat.Text, null,
        AllowedTools: ["parallel_web_search", "parallel_web_fetch"], IsInteractive: false);

    try
    {
        var envelope = await ParallelSearchExample.ExecuteAsync(toolName, arguments, context, cancellation.Token);
        Console.WriteLine(envelope.Result.Output ?? envelope.Result.ErrorMessage);
        Environment.ExitCode = envelope.Result.Succeeded ? 0 : 1;
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Console.Error.WriteLine(exception.Message);
        Environment.ExitCode = 1;
    }
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSharpClawRuntime(builder.Configuration);

// Register the custom echo tool so the agent can invoke it during turns.
builder.Services.AddSingleton<EchoTool>();
builder.Services.AddSingleton<ISharpClawTool>(sp => sp.GetRequiredService<EchoTool>());

using var host = builder.Build();
await host.StartAsync();

var runtime = host.Services.GetRequiredService<IConversationRuntime>();

var workspacePath = Directory.GetCurrentDirectory();
var session = await runtime.CreateSessionAsync(
    workspacePath,
    PermissionMode.ReadOnly,
    OutputFormat.Text,
    CancellationToken.None);

// Ask the agent to use the echo tool.
var request = new RunPromptRequest(
    Prompt: "Use the echo tool to echo the message: Hello from SharpClaw!",
    SessionId: session.Id,
    WorkingDirectory: workspacePath,
    PermissionMode: PermissionMode.ReadOnly,
    OutputFormat: OutputFormat.Text,
    Metadata: null);

var result = await runtime.RunPromptAsync(request, CancellationToken.None);

Console.WriteLine(result.FinalOutput ?? "(no output)");

await host.StopAsync();
