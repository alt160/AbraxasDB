using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Store scenario exposing durable metadata for several typed stores as separate results.<br/></summary>
internal sealed class InspectStoreDefinitionsScenario : IShowcaseScenario
{
    public string Id => "inspect-store-definitions";
    public string Category => "Stores";
    public string Title => "Inspect typed store definitions";
    public string Description => "Read the durable metadata for two stores and explore each StoreInfo independently.";
    public string Cost => "Immediate";
    public bool Mutates => false;
    public ScenarioDocumentation Documentation { get; } = new(
        "Want to inspect a store's setup without reading its records? GetStoreInfo returns its saved name, description, declared type information, state, and indexes. That store-level type information is not CLR type metadata embedded in every record.",
        ["store metadata", "declared record type", "store lifecycle"],
        ["RecordBase.GetStoreInfo", "RecordBase.StoreInfo"],
        ["store", "metadata", "catalog", "type"],
        ["Create and reuse a typed RecordStore"],
        210);
    public string SourceCode => ExampleSource.Read("Stores/InspectStoreDefinitions.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>Reads two public store definitions and returns each as an independently explorable result.<br/></summary>
    /// <param name="workspace">Prepared Stores group workspace.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before metadata access.<br/></param>
    /// <returns>Two retained StoreInfo objects plus one scenario-wide store count.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        RecordBase.StoreInfo customerStore = workspace.RecordBase.GetStoreInfo("customers") ?? throw new InvalidOperationException("The customers store is missing.");
        RecordBase.StoreInfo orderStore = workspace.RecordBase.GetStoreInfo("orders") ?? throw new InvalidOperationException("The orders store is missing.");
        timer.Stop();
        return new ScenarioResult(true, "Both durable typed-store definitions were returned.", [new ScenarioFact("Store definitions", "2")],
            [new ScenarioResultItem("Customers store", null, customerStore, typeof(RecordBase.StoreInfo)), new ScenarioResultItem("Orders store", null, orderStore, typeof(RecordBase.StoreInfo))], timer.Elapsed.TotalMilliseconds);
    }
}
