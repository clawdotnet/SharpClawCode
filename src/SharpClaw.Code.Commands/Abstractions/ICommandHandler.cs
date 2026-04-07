using System.CommandLine;
using SharpClaw.Code.Commands.Options;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Builds a top-level CLI command.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the command name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the command description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Builds the command instance.
    /// </summary>
    /// <param name="globalOptions">The shared global options.</param>
    /// <returns>A configured command.</returns>
    Command BuildCommand(GlobalCliOptions globalOptions);
}
