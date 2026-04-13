using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSharpClawRuntime(builder.Configuration);

var app = builder.Build();

app.MapPost("/chat", async (ChatRequest body, IConversationRuntime runtime, CancellationToken ct) =>
{
    var workspacePath = Directory.GetCurrentDirectory();

    string sessionId;
    if (!string.IsNullOrWhiteSpace(body.SessionId))
    {
        sessionId = body.SessionId;
    }
    else
    {
        var session = await runtime.CreateSessionAsync(
            workspacePath,
            PermissionMode.ReadOnly,
            OutputFormat.Text,
            ct);
        sessionId = session.Id;
    }

    var request = new RunPromptRequest(
        Prompt: body.Prompt,
        SessionId: sessionId,
        WorkingDirectory: workspacePath,
        PermissionMode: PermissionMode.ReadOnly,
        OutputFormat: OutputFormat.Text,
        Metadata: null);

    var result = await runtime.RunPromptAsync(request, ct);

    return Results.Ok(new ChatResponse(result.FinalOutput ?? string.Empty, sessionId));
});

app.Run();

record ChatRequest(string Prompt, string? SessionId);
record ChatResponse(string Output, string SessionId);
