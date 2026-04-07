namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Classifies how a tracked file mutation should be inverted or replayed.
/// </summary>
public enum FileMutationKind
{
    /// <summary>
    /// The file did not exist before the operation; inverse deletes the file after verifying content.
    /// </summary>
    Create,

    /// <summary>
    /// The file existed and was fully replaced or created via overwrite.
    /// </summary>
    Replace,

    /// <summary>
    /// The file was removed by SharpClaw; inverse restores <see cref="FileMutationOperation.ContentAfter"/> (stored prior contents).
    /// </summary>
    Delete,
}
