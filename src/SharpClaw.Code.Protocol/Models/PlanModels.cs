namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Structured plan task emitted by plan-mode generation.
/// </summary>
/// <param name="Id">Stable plan task identifier.</param>
/// <param name="Title">Short actionable task title.</param>
/// <param name="Status">Desired todo status for the task.</param>
/// <param name="Details">Optional execution details or rationale.</param>
/// <param name="DoneCriteria">Optional completion criteria.</param>
public sealed record PlanTaskItem(
    string Id,
    string Title,
    TodoStatus Status,
    string? Details,
    string? DoneCriteria);

/// <summary>
/// JSON contract expected from deep plan generation.
/// </summary>
/// <param name="Summary">High-level plan summary.</param>
/// <param name="Assumptions">Important assumptions the plan depends on.</param>
/// <param name="Risks">Key delivery or implementation risks.</param>
/// <param name="NextAction">Highest-leverage next action to execute.</param>
/// <param name="Tasks">Actionable implementation tasks.</param>
public sealed record PlanGenerationPayload(
    string Summary,
    List<string> Assumptions,
    List<string> Risks,
    string NextAction,
    List<PlanTaskItem> Tasks);

/// <summary>
/// Durable todo seed used to synchronize planning-owned session todos.
/// </summary>
/// <param name="ExternalId">Stable external task identifier.</param>
/// <param name="Title">Todo title to surface in the session.</param>
/// <param name="Status">Todo status to apply.</param>
public sealed record ManagedTodoSeed(
    string ExternalId,
    string Title,
    TodoStatus Status);

/// <summary>
/// Summarizes the effects of synchronizing managed session todos.
/// </summary>
/// <param name="OwnerAgentId">Stable owner used for the synchronized todo set.</param>
/// <param name="AddedCount">Number of new todos created.</param>
/// <param name="UpdatedCount">Number of existing todos updated.</param>
/// <param name="RemovedCount">Number of stale managed todos removed.</param>
/// <param name="ActiveTodos">Current managed todos after synchronization.</param>
public sealed record ManagedTodoSyncResult(
    string OwnerAgentId,
    int AddedCount,
    int UpdatedCount,
    int RemovedCount,
    IReadOnlyList<TodoItem> ActiveTodos);

/// <summary>
/// Structured plan result returned from plan-mode execution.
/// </summary>
/// <param name="Summary">High-level plan summary.</param>
/// <param name="Assumptions">Important assumptions the plan depends on.</param>
/// <param name="Risks">Key delivery or implementation risks.</param>
/// <param name="NextAction">Highest-leverage next action to execute.</param>
/// <param name="Tasks">Structured plan tasks.</param>
/// <param name="TodoSync">Result of synchronizing planning-owned session todos.</param>
public sealed record PlanExecutionResult(
    string Summary,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    string NextAction,
    IReadOnlyList<PlanTaskItem> Tasks,
    ManagedTodoSyncResult TodoSync);
