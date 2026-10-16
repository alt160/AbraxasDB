using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Environment scenario proving typed index creation and idempotent reuse.<br/></summary>
internal sealed class CreateRecordIndexesScenario : IShowcaseScenario
{
    public string Id => "create-record-indexes";
    public string Category => "Recordbase Fundamentals";
    public string Title => "Create and reuse RecordStore indexes";
    public string Description => "Create a typed store, add durable member indexes, and repeat the declarations without duplicating them.";
    public string Cost => "Depends on existing store population";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need to look up customers by Code or Region? Add an index for each member on the customer store. Repeating the same CreateIndex calls opens the existing definitions rather than adding duplicate indexes; changing a definition is a different operation.",
        ["index lifecycle", "typed member selectors", "durable indexes", "repeatable setup"],
        ["RecordStore<T>.CreateIndex", "RecordStore<T>.GetIndexNames", "IndexDirection"],
        ["index", "create index", "reuse index", "selector", "ordered access"],
        ["Create and reuse a typed RecordStore"],
        30);
    public string SourceCode => ExampleSource.Read("Fundamentals/CreateRecordIndexes.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>Runs the exact index setup example against the freshly emptied fundamentals folder.<br/></summary>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        IndexFoundationProof proof = CreateAndReuseIndexes(workspace.DirectoryPath);
        timer.Stop();
        bool passed = proof.ReusedWithoutDuplication && proof.IndexNames.Length == 2;
        return new ScenarioResult(
            passed,
            passed ? "The indexes were created once and reused when the declarations ran again." : "The repeated declarations did not return the expected indexes.",
            [
                new ScenarioFact("Store", proof.StoreName),
                new ScenarioFact("Indexes", proof.IndexNames.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new ScenarioFact("Repeated declarations", proof.ReusedWithoutDuplication ? "No duplicates" : "Index list changed")
            ],
            proof,
            typeof(IndexFoundationProof),
            timer.Elapsed.TotalMilliseconds);
    }

// <showcase-code>
    /// <summary>
    /// Creates a typed store and two durable indexes, then repeats the declarations safely.<br/>
    /// Member selectors preserve ordinary C# type checking while Abraxas owns the index lifecycle.<br/>
    /// </summary>
    /// <param name="directory">Folder that owns this scenario's RecordBase files.<br/></param>
    /// <returns>Detached typed-store and index evidence.<br/></returns>
    public static IndexFoundationProof CreateAndReuseIndexes(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>(
            "customers",
            "Showcase customers");

        customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
        customers.CreateIndex(customer => customer.Region, IndexDirection.Ascending);
        string[] firstDeclaration = customers.GetIndexNames();

        customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
        customers.CreateIndex(customer => customer.Region, IndexDirection.Ascending);
        string[] repeatedDeclaration = customers.GetIndexNames();

        return new IndexFoundationProof
        {
            StoreName = "customers",
            RecordType = typeof(ShowcaseCustomer).FullName ?? nameof(ShowcaseCustomer),
            IndexNames = repeatedDeclaration,
            ReusedWithoutDuplication = firstDeclaration.Order().SequenceEqual(repeatedDeclaration.Order())
        };
    }
// </showcase-code>
}
