using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>First environment scenario proving creation, closure, and durable reopen in one owned folder.<br/></summary>
internal sealed class CreateRecordBaseScenario : IShowcaseScenario
{
    public string Id => "create-recordbase";
    public string Category => "Recordbase Fundamentals";
    public string Title => "Create and reopen a RecordBase";
    public string Description => "Create the files for a named RecordBase, close them, and open the same RecordBase again.";
    public string Cost => "Immediate when empty";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need a durable home for records? CreateOrOpen creates the named RecordBase files when they are missing, or opens them when they exist. This example closes and reopens those same files to show that the data outlives the first object instance.",
        ["RecordBase lifecycle", "durable files", "create or open", "explicit disposal"],
        ["RecordBase.CreateOrOpen", "RecordBase.Dispose"],
        ["create", "open", "reopen", "folder", "files", "lifecycle"],
        [],
        10);
    public string SourceCode => ExampleSource.Read("Fundamentals/CreateRecordBase.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>Runs the exact creation example against the freshly emptied fundamentals folder.<br/></summary>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        RecordBaseLifecycleProof proof = CreateAndReopenRecordBase(workspace.DirectoryPath);
        timer.Stop();
        bool passed = proof.Reopened && proof.DurableFileCount > 0;
        return new ScenarioResult(
            passed,
            passed ? "The RecordBase was created, closed, and reopened from the same durable files." : "The RecordBase lifecycle proof was incomplete.",
            [
                new ScenarioFact("Operation", "CreateOrOpen → Dispose → CreateOrOpen"),
                new ScenarioFact("Durable files", proof.DurableFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new ScenarioFact("Stores before setup", proof.ActiveStoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ],
            proof,
            typeof(RecordBaseLifecycleProof),
            timer.Elapsed.TotalMilliseconds);
    }

// <showcase-code>
    /// <summary>
    /// Creates or opens one RecordBase, closes it, and then reopens the same durable environment.<br/>
    /// The same call intentionally serves first-run creation and later application starts.<br/>
    /// </summary>
    /// <param name="directory">Folder that owns this RecordBase's durable files.<br/></param>
    /// <returns>Lifecycle evidence collected after the reopened RecordBase is available.<br/></returns>
    public static RecordBaseLifecycleProof CreateAndReopenRecordBase(string directory)
    {
        using (RecordBase created = RecordBase.CreateOrOpen(directory, "showcase"))
        {
        }

        using RecordBase reopened = RecordBase.CreateOrOpen(directory, "showcase");
        return new RecordBaseLifecycleProof
        {
            Directory = directory,
            Name = "showcase",
            Reopened = true,
            DurableFileCount = Directory.EnumerateFiles(directory).Count(),
            ActiveStoreCount = reopened.GetStoreInfos()?.Count ?? 0
        };
    }
// </showcase-code>
}
