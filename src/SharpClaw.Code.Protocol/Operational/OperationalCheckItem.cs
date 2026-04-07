namespace SharpClaw.Code.Protocol.Operational;

/// <summary>
/// One row in an operational report (doctor/status).
/// </summary>
/// <param name="Id">Stable machine id (e.g. <c>workspace.access</c>).</param>
/// <param name="Status">Check outcome.</param>
/// <param name="Summary">One-line human summary.</param>
/// <param name="Detail">Optional elaboration or error text.</param>
public sealed record OperationalCheckItem(
    string Id,
    OperationalCheckStatus Status,
    string Summary,
    string? Detail = null);
