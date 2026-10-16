using System.Reflection;

namespace AbraxasShowcase;

/// <summary>Reads the exact marked method text from the same embedded C# file compiled into the application.<br/></summary>
internal static class ScenarioSource
{
    /// <summary>
    /// Extracts the single showcase-code region from an embedded compiled scenario source file.<br/>
    /// Missing or duplicate markers fail visibly so displayed code cannot silently drift to unrelated text.<br/>
    /// </summary>
    /// <param name="fileName">Unique source filename suffix embedded by the project.<br/></param>
    /// <returns>The exact source characters between the showcase markers, with outer blank lines trimmed.<br/></returns>
    internal static string Read(string fileName)
    {
        Assembly assembly = typeof(ScenarioSource).Assembly;
        string[] matches = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(fileName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one embedded scenario source ending in '{fileName}', found {matches.Length}.");
        using Stream stream = assembly.GetManifestResourceStream(matches[0]) ?? throw new InvalidOperationException("Scenario source is unavailable.");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();
        const string start = "// <showcase-code>";
        const string end = "// </showcase-code>";
        int startIndex = source.IndexOf(start, StringComparison.Ordinal);
        int endIndex = source.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex || source.IndexOf(start, startIndex + start.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Scenario source markers are missing, reversed, or duplicated.");
        startIndex += start.Length;
        return source[startIndex..endIndex].Trim('\r', '\n');
    }
}
