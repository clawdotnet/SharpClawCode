namespace SharpClaw.Code.Protocol.Operational;

/// <summary>
/// Machine-stable doctor output (schema versioned).
/// </summary>
/// <param name="SchemaVersion">Contract version (currently <c>1.0</c>).</param>
/// <param name="GeneratedAtUtc">When the report was built.</param>
/// <param name="OverallStatus">Worst status across checks.</param>
/// <param name="WorkspaceRoot">Normalized workspace path.</param>
/// <param name="Checks">Ordered diagnostic checks.</param>
/// <param name="ConfigurationKeysSample">Optional sample of resolved configuration keys (no secret values).</param>
public sealed record DoctorReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    OperationalCheckStatus OverallStatus,
    string WorkspaceRoot,
    IReadOnlyList<OperationalCheckItem> Checks,
    IReadOnlyDictionary<string, string>? ConfigurationKeysSample = null);
