namespace AbraxasShowcase;

/// <summary>Exports complete paste-ready scenario programs without involving the desktop host.<br/></summary>
internal static class ExampleExporter
{
    /// <summary>
    /// Exports every runnable scenario as one complete paste-ready <c>Program.cs</c> document.<br/>
    /// The caller must provide an explicit output directory; each scenario is written beneath its stable identifier so validation can compile the documents independently.<br/>
    /// Existing files with the same generated names are replaced, while unrelated files and directories are left untouched.<br/>
    /// </summary>
    /// <param name="args">Command-line arguments containing <c>--output</c> followed by the exact export directory.<br/></param>
    internal static void Export(string[] args)
    {
        string outputDirectory = Path.GetFullPath(CommandLine.Argument(args, "--output")
            ?? throw new ArgumentException("--export-examples requires --output followed by a directory."));
        Directory.CreateDirectory(outputDirectory);
        foreach (IShowcaseScenario scenario in ScenarioCatalog.All)
        {
            string scenarioDirectory = Path.Combine(outputDirectory, scenario.Id);
            Directory.CreateDirectory(scenarioDirectory);
            File.WriteAllText(Path.Combine(scenarioDirectory, "Program.cs"), scenario.CompleteSource);
            if (ScenarioGroupCatalog.ForCategory(scenario.Category).CommercialOnly)
                File.WriteAllText(Path.Combine(scenarioDirectory, "requires-commercial-edition.txt"), "Commercial Abraxas payload APIs are required. The source remains readable in FOSS.");
        }
    }
}
