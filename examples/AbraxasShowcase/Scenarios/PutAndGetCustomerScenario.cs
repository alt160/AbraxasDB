using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Foundational mutation scenario proving external identity assignment and exact round-trip retrieval.<br/></summary>
internal sealed class PutAndGetCustomerScenario : IShowcaseScenario
{
    public string Id => "put-get-customer";
    public string Category => "Record persistence basics";
    public string Title => "Put and get an ordinary C# object";
    public string Description => "Put an ordinary C# object, keep the returned RecordID, and get the object again by that ID.";
    public string Cost => "Immediate on every laboratory size";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need the simplest save-and-read loop? Put stores an ordinary customer and returns its RecordID. Get reads it back by that ID. Expand the result to inspect the real .NET object.",
        ["POCO persistence", "external identity", "hydration", "execution proof"],
        ["RecordStore<T>.Put", "RecordStore<T>.Get", "RecordID"],
        ["insert", "round trip", "save", "load", "object"],
        ["Prepare Record persistence basics"],
        110);
    public string SourceCode => ExampleSource.Read("RecordPersistence/PutAndGetCustomer.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Runs the showcase orchestration around the focused Abraxas example and produces inspection evidence.<br/>
    /// </summary>
    /// <param name="workspace">Active shared RecordBase laboratory selected by the developer.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before the mutation begins.<br/></param>
    /// <returns>Measured identity and round-trip evidence for the results panel.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        (ulong identity, ShowcaseCustomer customer, ShowcaseCustomer roundTrip) = PutAndGetCustomer(workspace.Customers);
        timer.Stop();

        bool passed = roundTrip.Code == customer.Code
            && roundTrip.Name == customer.Name
            && roundTrip.Region == customer.Region
            && roundTrip.Tags.SequenceEqual(customer.Tags);
        return new ScenarioResult(
            passed,
            passed ? "The object round-tripped exactly." : "The retrieved object differed from the submitted object.",
            [
                new ScenarioFact("Operation", "Put → Get")
            ],
            roundTrip,
            typeof(ShowcaseCustomer),
            timer.Elapsed.TotalMilliseconds,
            identity,
            "Retrieved customer");
    }

// <showcase-code>
    /// <summary>
    /// Persists one ordinary customer and retrieves it by the external identity returned from <c>Put</c>.<br/>
    /// The returned tuple keeps the submitted object, its Abraxas RecordID, and the hydrated object available to the caller.<br/>
    /// </summary>
    /// <param name="customers">Typed RecordStore that owns <see cref="ShowcaseCustomer"/> records.<br/></param>
    /// <returns>The external RecordID, submitted customer, and completely hydrated customer.<br/></returns>
    public static (ulong RecordID, ShowcaseCustomer Submitted, ShowcaseCustomer Hydrated) PutAndGetCustomer(
        RecordStore<ShowcaseCustomer> customers)
    {
        var customer = new ShowcaseCustomer
        {
            Code = "LIVE-" + DateTime.UtcNow.Ticks,
            Name = "Live scenario customer",
            Region = "West",
            Tags = ["scenario", "round-trip"]
        };

        ulong identity = customers.Put(customer);
        ShowcaseCustomer roundTrip = customers.Get(identity);
        return (identity, customer, roundTrip);
    }
// </showcase-code>
}
