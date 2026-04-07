using System.CommandLine;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Commands.Options;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Runs SharpClaw as an ACP-compatible subprocess over stdio (JSON-RPC, newline-delimited).
/// </summary>
public sealed class AcpCommandHandler(AcpStdioHost acpHost) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "acp";

    /// <inheritdoc />
    public string Description => "Agent Client Protocol mode (stdin/stdout JSON-RPC). See docs/acp.md.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var cmd = new Command(Name, Description);
        foreach (var opt in globalOptions.All)
        {
            cmd.Options.Add(opt);
        }

        cmd.SetAction(async (_, cancellationToken) =>
        {
            await acpHost.RunAsync(Console.In, Console.Out, cancellationToken).ConfigureAwait(false);
            return 0;
        });
        return cmd;
    }
}
