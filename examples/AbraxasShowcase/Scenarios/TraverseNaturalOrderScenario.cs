using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Shows a bounded forward-only traversal in the source order selected by Abraxas.<br/></summary>
internal sealed class TraverseNaturalOrderScenario : IShowcaseScenario
{
    public string Id => "read-natural-order";
    public string Category => "Retrieval and readers";
    public string Title => "Traverse records in natural order";
    public string Description => "Read the first eight customers returned by a typed reader and stop without scanning the rest.";
    public string Cost => "Scales with records visited";
    public bool Mutates => false;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need to process customers one at a time? Readers.Open starts a forward-only read; Read advances, and ID and Get give you the current RecordID and object. Stop after eight instead of visiting the rest. Without an explicit sort, do not assume insertion or RecordID order.",
        ["forward-only reader", "natural source order", "early exit"],
        ["RecordStore<T>.Readers.Open", "DataReader<T>.Read", "DataReader<T>.ID", "DataReader<T>.Get"],
        ["reader", "iterate", "stream", "natural order", "stop early"],
        ["Prepare Retrieval and readers"],
        310);
    public string SourceCode => ExampleSource.Read("Retrieval/ReadNaturalOrder.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Opens the prepared customers store as a typed forward-only reader and retains at most eight actual result objects.<br/>
    /// The loop stops after the requested sample even when more records remain, without asserting an order Abraxas has not promised.<br/>
    /// </summary>
    /// <param name="workspace">Prepared Retrieval and readers group workspace.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between reader advances.<br/></param>
    /// <returns>Actual customer objects and RecordIDs in the order read, plus bounded traversal evidence.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        const int sampleLimit = 8;
        var timer = Stopwatch.StartNew();
        var results = new List<ScenarioResultItem>(sampleLimit);
        using var reader = workspace.Customers.Readers.Open();
        while (results.Count < sampleLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
            results.Add(new ScenarioResultItem($"Customer {results.Count + 1}", reader.ID, reader.Get(), typeof(ShowcaseCustomer)));
        }
        timer.Stop();
        if (results.Count == 0)
            throw new InvalidOperationException("The prepared Retrieval and readers group contains no customers.");
        return new ScenarioResult(true, $"Read {results.Count} customers and stopped the reader.",
            [new ScenarioFact("Reader", "Readers.Open → Read → Get"),
             new ScenarioFact("Records visited", results.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
             new ScenarioFact("Order", "Natural source order; no sort requested")],
            results, timer.Elapsed.TotalMilliseconds);
    }
}
