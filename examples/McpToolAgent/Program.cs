using McpToolAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Composition;
using SharpClaw.Code.Tools.Abstractions;

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
