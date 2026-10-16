namespace AbraxasShowcase;

internal static class Program
{
    /// <summary>Starts the OSS Showcase or exports its readable, standalone example programs.<br/>
    /// This public application contains no private verification server or test entry points.<br/></summary>
    /// <param name="args">Optional --data directory, or --export-examples --output directory.<br/></param>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--export-examples", StringComparer.OrdinalIgnoreCase))
        {
            ExampleExporter.Export(args);
            return;
        }
        string applicationRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbraxasShowcase");
        string workspace = CommandLine.Argument(args, "--data") ?? Path.Combine(applicationRoot, "laboratory");
        Application.Run(new MainWindow(workspace, Path.Combine(applicationRoot, "webview")));
    }
}
