using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Composition;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: MinimalConsoleAgent <prompt>");
    return 1;
}

var prompt = string.Join(' ', args);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSharpClawRuntime(builder.Configuration);

using var host = builder.Build();
await host.StartAsync();

var runtime = host.Services.GetRequiredService<IConversationRuntime>();

var workspacePath = Directory.GetCurrentDirectory();
var session = await runtime.CreateSessionAsync(
    workspacePath,
    PermissionMode.ReadOnly,
    OutputFormat.Text,
    CancellationToken.None);

var request = new RunPromptRequest(
    Prompt: prompt,
    SessionId: session.Id,
    WorkingDirectory: workspacePath,
    PermissionMode: PermissionMode.ReadOnly,
    OutputFormat: OutputFormat.Text,
    Metadata: null);

var result = await runtime.RunPromptAsync(request, CancellationToken.None);

Console.WriteLine(result.FinalOutput ?? "(no output)");

await host.StopAsync();
return 0;
