using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Foundational environment scenario proving low-friction typed-store creation-or-open and explicit reopen semantics.<br/></summary>
internal sealed class ReuseTypedRecordStoreScenario : IShowcaseScenario
{
    public string Id => "create-typed-recordstore";
    public string Category => "Recordbase Fundamentals";
    public string Title => "Create and reuse a typed RecordStore";
    public string Description => "Create a typed customer store, open it again by name, and confirm both variables refer to the same stored records.";
    public string Cost => "Immediate";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Need a named home for customer records across application runs? CreateOrOpenStore<T> creates or opens it. OpenStore<T> is for a store you expect to exist already; this example uses both calls on the same named store.",
        ["typed stores", "create or open", "explicit open", "durable store identity"],
        ["RecordBase.CreateOrOpenStore<T>", "RecordBase.OpenStore<T>", "RecordStore<T>.Aggregates.Count"],
        ["store", "typed", "poco", "setup", "reuse", "reopen"],
        ["Create and reopen a RecordBase"],
        20);
    public string SourceCode => ExampleSource.Read("Fundamentals/CreateTypedRecordStore.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Runs the showcase orchestration around the focused Abraxas example and produces inspection evidence.<br/>
    /// </summary>
    /// <param name="workspace">Showcase-owned folder supplied to the self-contained environment scenario.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before typed-store resolution begins.<br/></param>
    /// <returns>Measured typed-store reuse evidence retained for live inspection.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        TypedStoreReuseProof proof = CreateAndOpenCustomerStore(workspace.DirectoryPath);
        timer.Stop();

        return new ScenarioResult(
            proof.SameDurableStore,
            proof.SameDurableStore ? "Both variables opened the same customer store." : "The two store variables did not report matching records.",
            [
                new ScenarioFact("Operations", "CreateOrOpenStore<T> → OpenStore<T>"),
                new ScenarioFact("Records through each variable", $"{proof.CreateOrOpenCount} → {proof.OpenCount}"),
                new ScenarioFact("Persistent indexes", proof.IndexNames.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ],
            proof,
            typeof(TypedStoreReuseProof),
            timer.Elapsed.TotalMilliseconds);
    }

// <showcase-code>
    /// <summary>
    /// Resolves one durable typed store through create-or-open and then through an explicit open operation.<br/>
    /// Returning both typed facades and the public store metadata lets the caller use or compare the shared durable store immediately.<br/>
    /// </summary>
    /// <param name="directory">Folder that owns this scenario's RecordBase files.<br/></param>
    /// <returns>Detached evidence proving both typed facades resolved the same durable store.<br/></returns>
    public static TypedStoreReuseProof CreateAndOpenCustomerStore(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> createOrOpen = recordBase.CreateOrOpenStore<ShowcaseCustomer>(
            "customers",
            "Showcase customers");
        RecordStore<ShowcaseCustomer> open = recordBase.OpenStore<ShowcaseCustomer>("customers");
        RecordBase.StoreInfo store = recordBase.GetStoreInfo("customers")
            ?? throw new InvalidOperationException("The customer store metadata is unavailable.");
        ulong createOrOpenCount = createOrOpen.Aggregates.Count();
        ulong openCount = open.Aggregates.Count();
        return new TypedStoreReuseProof
        {
            StoreName = store.Name,
            RecordType = store.ItemType?.FullName ?? "Unavailable",
            InstanceID = store.InstanceID,
            CreateOrOpenCount = createOrOpenCount,
            OpenCount = openCount,
            IndexNames = open.GetIndexNames(),
            SameDurableStore = createOrOpenCount == openCount
                && store.ItemType == typeof(ShowcaseCustomer)
                && store.InstanceID != Guid.Empty
        };
    }
// </showcase-code>
}
