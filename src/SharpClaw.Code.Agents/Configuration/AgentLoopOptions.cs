namespace SharpClaw.Code.Agents.Configuration;

/// <summary>
/// Configures the tool-calling loop executed by <see cref="Internal.ProviderBackedAgentKernel"/>.
/// </summary>
public sealed class AgentLoopOptions
{
    /// <summary>
    /// The maximum number of tool-calling iterations before the loop is forcefully terminated.
    /// </summary>
    public int MaxToolIterations { get; set; } = 25;

    /// <summary>
    /// The maximum number of tokens to request per provider call.
    /// </summary>
    public int MaxTokensPerRequest { get; set; } = 16_384;
}
