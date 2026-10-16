using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Foundational read scenario proving public discovery of the active durable RecordBase stores.<br/></summary>
internal sealed class InspectActiveRecordBaseScenario : IShowcaseScenario
{
    public string Id => "inspect-active-recordbase";
    public string Category => "Recordbase Fundamentals";
    public string Title => "Inspect the active RecordBase";
    public string Description => "List the stores in an open RecordBase and inspect the public information Abraxas keeps for each store.";
    public string Cost => "Immediate";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Want to see which stores this RecordBase contains? Create a typed store, then call GetStoreInfos. The returned StoreInfo values describe the stores without opening and reading every record.",
        ["RecordBase discovery", "store metadata", "durable identity", "lifecycle"],
        ["RecordBase.GetStoreInfos", "RecordBase.StoreInfo", "StoreLifecycleState"],
        ["inspect", "list stores", "metadata", "schema", "catalog"],
        ["Create and reuse RecordStore indexes"],
        41);
    public string SourceCode => ExampleSource.Read("Fundamentals/InspectActiveRecordBase.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Runs the showcase orchestration around the focused Abraxas example and produces inspection evidence.<br/>
    /// </summary>
    /// <param name="workspace">Showcase-owned folder supplied to the self-contained environment scenario.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before metadata discovery begins.<br/></param>
    /// <returns>Measured public store-catalog evidence retained for live inspection.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        using RecordBase recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
        RecordBase.StoresInfo stores = InspectActiveRecordBase(recordBase);
        var proof = new RecordBaseFoundationProof
        {
            Directory = workspace.DirectoryPath,
            State = "Open",
            Stores = stores.Values
                .OrderBy(store => store.Name, StringComparer.OrdinalIgnoreCase)
                .Select(store => new StoreFoundationInfo
                {
                    Name = store.Name,
                    Description = store.Description ?? "",
                    RecordType = store.ItemType?.FullName ?? "Unavailable",
                    Lifecycle = store.Lifecycle.ToString(),
                    InstanceID = store.InstanceID
                })
                .ToArray()
        };
        timer.Stop();

        bool passed = proof.Stores.Length > 0
            && proof.Stores.All(store => store.Lifecycle == StoreLifecycleState.Active.ToString())
            && proof.Stores.All(store => store.InstanceID != Guid.Empty);
        return new ScenarioResult(
            passed,
            passed ? "The active RecordBase exposed its durable typed-store catalog." : "The expected active laboratory stores were not discovered.",
            [
                new ScenarioFact("Operation", "GetStoreInfos"),
                new ScenarioFact("Active stores", proof.Stores.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new ScenarioFact("Result", "StoreInfo values")
            ],
            proof,
            typeof(RecordBaseFoundationProof),
            timer.Elapsed.TotalMilliseconds);
    }

// <showcase-code>
    /// <summary>
    /// Reads detached public metadata for every active store in an open RecordBase.<br/>
    /// The returned collection can be enumerated or queried without exposing internal engine state.<br/>
    /// </summary>
    /// <param name="recordBase">Open RecordBase whose active typed stores should be discovered.<br/></param>
    /// <returns>Detached public metadata keyed by durable store name.<br/></returns>
    public static RecordBase.StoresInfo InspectActiveRecordBase(RecordBase recordBase)
    {
        return recordBase.GetStoreInfos() ?? new RecordBase.StoresInfo();
    }
// </showcase-code>
}
