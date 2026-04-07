namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Optional editor selection handed to the runtime (character offsets in UTF-16).
/// </summary>
/// <param name="Start">Inclusive start offset.</param>
/// <param name="End">Exclusive end offset.</param>
/// <param name="Text">Selected text when provided by the editor.</param>
public sealed record TextSelectionRange(int Start, int End, string? Text);
