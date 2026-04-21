using SharpClaw.Code;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: MinimalConsoleAgent <prompt>");
    return 1;
}

var prompt = string.Join(' ', args);

await using var runtimeHost = new SharpClawRuntimeHostBuilder(args).Build();
await runtimeHost.StartAsync();

var workspacePath = Directory.GetCurrentDirectory();
var hostContext = new RuntimeHostContext(
    HostId: "minimal-console-agent",
    IsEmbeddedHost: true);
var session = await runtimeHost.CreateSessionAsync(
    workspacePath,
    PermissionMode.ReadOnly,
    OutputFormat.Text,
    hostContext,
    CancellationToken.None);

var result = await runtimeHost.ExecutePromptAsync(
    prompt,
    workspacePath,
    model: "default",
    permissionMode: PermissionMode.ReadOnly,
    outputFormat: OutputFormat.Text,
    sessionId: session.Id,
    hostContext: hostContext,
    cancellationToken: CancellationToken.None);

Console.WriteLine(result.FinalOutput ?? "(no output)");

await runtimeHost.StopAsync();
return 0;
