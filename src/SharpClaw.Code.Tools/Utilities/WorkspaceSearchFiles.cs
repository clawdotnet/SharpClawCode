namespace SharpClaw.Code.Tools.Utilities;

/// <summary>Enumerates source files without following links or descending into generated dependency trees.</summary>
internal static class WorkspaceSearchFiles
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".sharpclaw", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "coverage", ".venv" };

    internal static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
            foreach (var file in Directory.EnumerateFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
        }
    }
}
