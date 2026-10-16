using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Store scenario showing opt-in observation of a completed persistence operation.<br/></summary>
internal sealed class ObserveStorePersistenceScenario : IShowcaseScenario
{
    public string Id => "observe-store-persistence";
    public string Category => "Stores";
    public string Title => "Observe a store persistence event";
    public string Description => "Subscribe to RecordPersisted, put one customer, and inspect the object associated with the completed write.";
    public string Cost => "Immediate";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need to react after a write completes? Subscribe to RecordPersisted. It reports the operation, RecordID, record type, and whether this was a new insert; ordinary writes do not require a listener.",
        ["store events", "persistence lifecycle", "opt-in observation"],
        ["RecordStore<T>.RecordPersisted", "RecordPersistedEventArgs", "RecordStore<T>.Put"],
        ["event", "persisted", "hook", "observe", "write"],
        ["Count records without hydrating them"],
        230);
    public string SourceCode => ExampleSource.Read("Stores/ObserveStorePersistence.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>Subscribes around one ordinary write and validates the event against the returned identity.<br/></summary>
    /// <param name="workspace">Prepared Stores group workspace.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before persistence begins.<br/></param>
    /// <returns>The persisted customer with event metadata shown as operation facts.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        RecordPersistedEventArgs? persisted = null;
        EventHandler<RecordPersistedEventArgs> handler = (_, eventArgs) => persisted = eventArgs;
        workspace.Customers.RecordPersisted += handler;
        var customer = new ShowcaseCustomer { Code = "EVENT-" + DateTime.UtcNow.Ticks, Name = "Persistence event customer", Region = "West", Tags = ["stores", "events"] };
        ulong recordID;
        try { recordID = workspace.Customers.Put(customer); }
        finally { workspace.Customers.RecordPersisted -= handler; }
        timer.Stop();
        bool passed = persisted is not null && persisted.RecordID == recordID && persisted.IsNewRecord;
        return new ScenarioResult(passed, passed ? "RecordPersisted described the completed insert." : "The persistence event did not match the completed write.",
            [new ScenarioFact("Event operation", persisted?.OperationName ?? "Not raised"), new ScenarioFact("New record", persisted?.IsNewRecord == true ? "Yes" : "No")],
            customer, typeof(ShowcaseCustomer), timer.Elapsed.TotalMilliseconds, recordID, "Persisted customer");
    }
}
