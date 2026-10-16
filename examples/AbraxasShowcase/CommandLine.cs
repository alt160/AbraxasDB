namespace AbraxasShowcase;

/// <summary>Provides the small command-line parsing contract shared by optional showcase routes.<br/></summary>
internal static class CommandLine
{
    /// <summary>Returns the value following one exact command-line switch, or <see langword="null"/> when the switch is absent.<br/></summary>
    /// <param name="args">Original command-line arguments.<br/></param>
    /// <param name="name">Exact switch name to locate.<br/></param>
    /// <returns>The following argument when present; otherwise <see langword="null"/>.<br/></returns>
    internal static string? Argument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
