using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Builds spec-mode prompt instructions and materializes generated spec artifacts into the workspace.
/// </summary>
public interface ISpecWorkflowService
{
    /// <summary>
    /// Builds the spec-mode output contract appended to the prompt context.
    /// </summary>
    /// <returns>The instruction block the model must follow.</returns>
    string BuildPromptInstructions();

    /// <summary>
    /// Parses a spec-mode model response and writes the generated artifacts into the workspace.
    /// </summary>
    /// <param name="workspacePath">Workspace root where spec documents should be created.</param>
    /// <param name="userPrompt">Original user prompt used to derive the spec slug.</param>
    /// <param name="modelOutput">Raw model output to parse.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Metadata describing the written artifact set.</returns>
    Task<SpecArtifactSet> MaterializeAsync(
        string workspacePath,
        string userPrompt,
        string modelOutput,
        CancellationToken cancellationToken);
}
