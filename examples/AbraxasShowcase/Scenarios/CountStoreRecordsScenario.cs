using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Store scenario proving counts do not require object hydration.<br/></summary>
internal sealed class CountStoreRecordsScenario : IShowcaseScenario
{
    public string Id => "count-store-records";
    public string Category => "Stores";
    public string Title => "Count records without hydrating them";
    public string Description => "Count customers and orders without reading every object into .NET.";
    public string Cost => "Immediate";
    public bool Mutates => false;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need a COUNT-style answer? Aggregates.Count returns each store's maintained total. Your code does not walk the store or build a .NET object for every record.",
        ["store population", "maintained aggregate", "no hydration"],
        ["RecordStore<T>.Aggregates.Count"],
        ["count", "records", "population", "aggregate"],
        ["Inspect typed store definitions"],
        220);
    public string SourceCode => ExampleSource.Read("Stores/CountStoreRecords.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>Reads both maintained store counts and returns them as scalar result items.<br/></summary>
    /// <param name="workspace">Prepared Stores group workspace.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before aggregate reads.<br/></param>
    /// <returns>Two scalar counts retained through the standard result collection.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        ulong customerCount = workspace.Customers.Aggregates.Count();
        ulong orderCount = workspace.Orders.Aggregates.Count();
        timer.Stop();
        return new ScenarioResult(true, "Both store populations were read without hydrating records.", [new ScenarioFact("Operation", "Aggregates.Count")],
            [new ScenarioResultItem("Customer count", null, customerCount, typeof(ulong)), new ScenarioResultItem("Order count", null, orderCount, typeof(ulong))], timer.Elapsed.TotalMilliseconds);
    }
}
