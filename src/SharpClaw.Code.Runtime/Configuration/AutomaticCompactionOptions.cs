namespace SharpClaw.Code.Runtime.Configuration;

/// <summary>Provider-context budgets. Configure exact provider/model keys when a model has a different context window.</summary>
public sealed class AutomaticCompactionOptions
{
    /// <summary>Enables automatic summarization at provider request boundaries.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Fallback total context window, including generated output.</summary>
    public int ContextWindowTokens { get; set; } = 128_000;
    /// <summary>Exact provider/model context-window overrides.</summary>
    public Dictionary<string, int> ModelContextWindows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Minimum output reservation, increased when the provider request asks for more.</summary>
    public int ReservedOutputTokens { get; set; } = 16_384;
    /// <summary>Fraction of available input budget at which maintenance starts.</summary>
    public double TriggerRatio { get; set; } = 0.8;
    /// <summary>Maximum generated summary tokens.</summary>
    public int SummaryMaxTokens { get; set; } = 4096;
    /// <summary>Recent messages retained verbatim, extended to keep tool exchanges intact.</summary>
    public int RetainedMessageCount { get; set; } = 6;
}
