using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Selects evaluated source owners and their reverse dependency closure.</summary>
public interface IAffectedProjectResolver
{
    /// <summary>Falls back to all loaded projects when ownership is unknown or configuration changed.</summary>
    AffectedProjectSelection Resolve(DotNetSolutionSummary solution, IReadOnlyList<string>? changedPaths);
}
