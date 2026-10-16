using AbraxasDB;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AbraxasShowcase;

/// <summary>Read-only startup classification for one candidate Showcase laboratory directory.<br/></summary>
internal sealed record LaboratoryPresence(
    string Directory,
    bool Exists,
    bool Owned,
    bool HasRecordBase,
    string State,
    string? GroupId,
    int? SetupVersion);

/// <summary>
/// Owns the active shared RecordBase laboratory used by every showcase scenario.<br/>
/// The host serializes access to this service, so scenario code can stay focused on ordinary public Abraxas calls.<br/>
/// </summary>
internal sealed partial class ShowcaseWorkspace : IDisposable
{
    private const string RecordBaseName = "showcase";
    private const string OwnershipFileName = ".abraxas-showcase-laboratory";
    private const string OwnershipContract = "AbraxasShowcase laboratory v1";
    private const string PresenceMetadataFileName = ".abraxas-showcase-state.json";
    private readonly string directory;
    private RecordBase recordBase = null!;
    private RecordStore<LaboratoryManifest> manifests = null!;
    private ulong manifestID;

    private RecordStore<ShowcaseCustomer>? customers;
    private RecordStore<ShowcaseOrder>? orders;
    private RecordStore<ShowcaseCustomerProfile>? profiles;
    private RecordStore<ShowcaseIdentityFreeNote>? notes;
    private RecordStore<ShowcaseHookDocument>? hookDocuments;
    private RecordStore<ShowcaseGraphCustomer>? graphCustomers;
    private RecordStore<ShowcaseGraphOrder>? graphOrders;
    private RecordStore<ShowcaseGraphLine>? graphLines;
    private RecordStore<ShowcasePathGraph>? pathGraphs;

    internal RecordStore<ShowcaseCustomer> Customers => customers
        ?? throw new InvalidOperationException("Prepare the RecordBase foundations group before using this scenario.");
    internal RecordStore<ShowcaseOrder> Orders => orders
        ?? throw new InvalidOperationException("Prepare the RecordBase foundations group before using this scenario.");
    internal RecordStore<ShowcaseCustomerProfile> Profiles => profiles
        ?? throw new InvalidOperationException("Prepare the Object shapes and identity group before using this scenario.");
    internal RecordStore<ShowcaseIdentityFreeNote> Notes => notes
        ?? throw new InvalidOperationException("Prepare the Object shapes and identity group before using this scenario.");
    internal RecordBase RecordBase => recordBase;
    internal RecordStore<ShowcaseHookDocument> HookDocuments => hookDocuments ?? throw new InvalidOperationException("Prepare Shape and materialization first.");
    internal RecordStore<ShowcaseGraphCustomer> GraphCustomers => graphCustomers ?? throw new InvalidOperationException("Prepare Object graphs across stores first.");
    internal RecordStore<ShowcaseGraphOrder> GraphOrders => graphOrders ?? throw new InvalidOperationException("Prepare Object graphs across stores first.");
    internal RecordStore<ShowcaseGraphLine> GraphLines => graphLines ?? throw new InvalidOperationException("Prepare Object graphs across stores first.");
    internal RecordStore<ShowcasePathGraph> PathGraphs => pathGraphs ?? throw new InvalidOperationException("Prepare Projection and value reads first.");
    internal string DirectoryPath => directory;

    /// <summary>Creates or opens the owned RecordBase and discovers stores belonging to any previously prepared scenario group.<br/></summary>
    /// <param name="directory">Dedicated application-owned directory containing only the active RecordBase files.<br/></param>
    internal ShowcaseWorkspace(string directory, bool openImmediately = true)
    {
        this.directory = Path.GetFullPath(directory);
        if (openImmediately) Open();
    }

    /// <summary>
    /// Inspects ownership and RecordBase presence without opening, creating, or mutating the directory.<br/>
    /// The host uses this before WebView2 startup so an existing laboratory is never silently opened or replaced.<br/>
    /// </summary>
    /// <param name="directory">Candidate dedicated laboratory directory.<br/></param>
    /// <returns>Ownership and existing-RecordBase classification for the startup decision UI.<br/></returns>
    internal static LaboratoryPresence Inspect(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath)) return new LaboratoryPresence(fullPath, false, false, false, "Absent", null, null);
        string marker = Path.Combine(fullPath, OwnershipFileName);
        bool owned = File.Exists(marker) && File.ReadAllText(marker) == OwnershipContract;
        bool hasEntries = Directory.EnumerateFileSystemEntries(fullPath).Any();
        bool hasRecordBase = owned && Directory.EnumerateFiles(fullPath, RecordBaseName + "*", SearchOption.TopDirectoryOnly).Any();
        string state = !hasEntries ? "Empty directory" : !owned ? "Unrecognized directory" : hasRecordBase ? "Existing RecordBase" : "Owned empty laboratory";
        string? groupId = null;
        int? setupVersion = null;
        string metadataPath = Path.Combine(fullPath, PresenceMetadataFileName);
        if (hasRecordBase && File.Exists(metadataPath))
        {
            try
            {
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                JsonElement root = metadata.RootElement;
                groupId = root.TryGetProperty("groupId", out JsonElement groupValue) ? groupValue.GetString() : null;
                setupVersion = root.TryGetProperty("setupVersion", out JsonElement versionValue) && versionValue.TryGetInt32(out int version)
                    ? version
                    : null;
            }
            catch (JsonException)
            {
                groupId = null;
                setupVersion = null;
            }
        }
        return new LaboratoryPresence(fullPath, true, owned, hasRecordBase, state, groupId, setupVersion);
    }

    /// <summary>Returns the persisted laboratory state plus live record counts and physical size.<br/></summary>
    /// <returns>A browser-safe snapshot describing the active shared scenario context.<br/></returns>
    internal object Describe()
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        return new
        {
            manifest.State,
            groupId = manifest.GroupID,
            setupVersion = manifest.SetupVersion,
            manifest.Size,
            manifest.Complexity,
            manifest.Seed,
            customers = customers?.Aggregates.Count() ?? 0,
            orders = orders?.Aggregates.Count() ?? 0,
            profiles = profiles?.Aggregates.Count() ?? 0,
            notes = notes?.Aggregates.Count() ?? 0,
            hookDocuments = hookDocuments?.Aggregates.Count() ?? 0,
            graphCustomers = graphCustomers?.Aggregates.Count() ?? 0,
            graphOrders = graphOrders?.Aggregates.Count() ?? 0,
            graphLines = graphLines?.Aggregates.Count() ?? 0,
            targetCustomers = manifest.CustomerCount,
            targetOrders = manifest.OrderCount,
            bytes = DirectoryBytes(),
            lastModifiedUtc = RecordBaseLastModifiedUtc(),
            manifest.GeneratedUtc,
            manifest.GenerationMilliseconds,
            lastScenarioId = manifest.LastScenarioID,
            lastScenarioTitle = manifest.LastScenarioTitle,
            directory
        };
    }

    /// <summary>
    /// Closes the current environment and replaces only the exact Showcase-owned folder before a fundamentals scenario runs.<br/>
    /// No RecordBase is opened here, allowing the learner-facing scenario to perform the first creation operation itself.<br/>
    /// </summary>
    internal void BeginFundamentalsScenario()
    {
        Close();
        DeleteOwnedDirectory(directory);
        EnsureOwnedDirectory();
    }

    /// <summary>Reopens the scenario-created RecordBase and records which fundamentals example produced the current files.<br/></summary>
    /// <param name="group">Exceptional replace-per-scenario group owning the folder.<br/></param>
    /// <param name="scenarioID">Stable identifier of the scenario that just ran.<br/></param>
    /// <param name="scenarioTitle">Developer-facing title of the scenario that just ran.<br/></param>
    /// <param name="succeeded">Whether the learner-facing operation completed successfully.<br/></param>
    internal void CompleteFundamentalsScenario(ScenarioGroupDefinition group, string scenarioID, string scenarioTitle, bool succeeded)
    {
        Open();
        LaboratoryManifest manifest = manifests.Get(manifestID);
        manifest.State = succeeded ? "Ready" : "Scenario failed";
        manifest.GroupID = group.Id;
        manifest.SetupVersion = group.SetupVersion;
        manifest.Size = "Scenario-owned";
        manifest.Complexity = "Lifecycle";
        manifest.GeneratedUtc = DateTime.UtcNow;
        manifest.LastScenarioID = scenarioID;
        manifest.LastScenarioTitle = scenarioTitle;
        manifests.Put(manifestID, manifest);
        WritePresenceMetadata(manifest);
    }

    /// <summary>
    /// Replaces the entire owned directory with one scenario group's environment and deterministic data.<br/>
    /// The previous RecordBase is closed and deleted at the file level before either displayed setup method runs.<br/>
    /// </summary>
    /// <param name="group">Registered group whose complete environment becomes active.<br/></param>
    /// <param name="request">Validated size, density, and seed parameters used by deterministic generation.<br/></param>
    /// <param name="progress">Optional setup progress callback forwarded to the displayed generation method.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before and during group generation.<br/></param>
    /// <returns>The refreshed active group description after both setup methods complete.<br/></returns>
    internal object PrepareGroup(
        ScenarioGroupDefinition group,
        GenerationRequest request,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        Validate(request);
        Close();
        DeleteOwnedDirectory(directory);
        EnsureOwnedDirectory();
        var timer = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(0, 1, "Creating the group RecordBase environment");
            group.CreateEnvironment(directory);
            group.GenerateData(directory, request, progress, cancellationToken);
            Open();
            LaboratoryManifest manifest = manifests.Get(manifestID);
            manifest.State = "Ready";
            manifest.GroupID = group.Id;
            manifest.SetupVersion = group.SetupVersion;
            manifest.Size = request.Size;
            manifest.Complexity = request.Complexity;
            manifest.Seed = request.Seed;
            manifest.CustomerCount = request.CustomerCount;
            manifest.OrderCount = checked(request.CustomerCount * request.OrdersPerCustomer);
            manifest.GeneratedUtc = DateTime.UtcNow;
            manifest.GenerationMilliseconds = timer.Elapsed.TotalMilliseconds;
            manifests.Put(manifestID, manifest);
            WritePresenceMetadata(manifest);
            return Describe();
        }
        catch
        {
            timer.Stop();
            Close();
            if (Directory.Exists(directory)) DeleteOwnedDirectory(directory);
            throw;
        }
    }

    /// <summary>Reports whether the active environment exactly satisfies one group's setup contract.<br/></summary>
    /// <param name="group">Registered group requested by a scenario or group page.<br/></param>
    /// <returns><see langword="true"/> only for a completed matching group and setup version.<br/></returns>
    internal bool IsGroupReady(ScenarioGroupDefinition group)
    {
        LaboratoryManifest manifest = manifests.Get(manifestID);
        return manifest.State == "Ready"
            && manifest.GroupID == group.Id
            && manifest.SetupVersion == group.SetupVersion;
    }

    /// <summary>Creates a bounded browser result from real hydrated records in the active group's stores.<br/></summary>
    /// <param name="sampleLimit">Maximum number of records retained from each group-owned typed store.<br/></param>
    /// <returns>Store counts plus real sample objects suitable for the live object explorer.<br/></returns>
    public void Dispose() => Close();

    /// <summary>Creates or opens the RecordBase plus private Showcase state, then discovers group stores without creating absent ones.<br/></summary>
    private void Open()
    {
        EnsureOwnedDirectory();
        recordBase = RecordBase.CreateOrOpen(directory, RecordBaseName);
        manifests = recordBase.CreateOrOpenStore<LaboratoryManifest>("laboratory", "Active showcase laboratory state");
        using var reader = manifests.Readers.Open();
        manifestID = reader.Read() ? reader.ID : manifests.Put(new LaboratoryManifest());
        customers = recordBase.StoreExists("customers") ? recordBase.OpenStore<ShowcaseCustomer>("customers") : null;
        orders = recordBase.StoreExists("orders") ? recordBase.OpenStore<ShowcaseOrder>("orders") : null;
        profiles = recordBase.StoreExists("customer-profiles") ? recordBase.OpenStore<ShowcaseCustomerProfile>("customer-profiles") : null;
        notes = recordBase.StoreExists("identity-free-notes") ? recordBase.OpenStore<ShowcaseIdentityFreeNote>("identity-free-notes") : null;
        hookDocuments = recordBase.StoreExists("hook-documents") ? recordBase.OpenStore<ShowcaseHookDocument>("hook-documents") : null;
        graphCustomers = recordBase.StoreExists("graph-customers") ? recordBase.OpenStore<ShowcaseGraphCustomer>("graph-customers") : null;
        graphOrders = recordBase.StoreExists("graph-orders") ? recordBase.OpenStore<ShowcaseGraphOrder>("graph-orders") : null;
        graphLines = recordBase.StoreExists("graph-lines") ? recordBase.OpenStore<ShowcaseGraphLine>("graph-lines") : null;
        pathGraphs = recordBase.StoreExists("path-graphs") ? recordBase.OpenStore<ShowcasePathGraph>("path-graphs") : null;
        if (hookDocuments is not null) MaterializationGroupSetup.Configure(hookDocuments);
        if (graphCustomers is not null && graphOrders is not null && graphLines is not null) ObjectGraphGroupSetup.Configure(graphCustomers, graphOrders, graphLines);
    }

    /// <summary>Disposes the open RecordBase once and leaves the service ready for a controlled reopen.<br/></summary>
    private void Close()
    {
        recordBase?.Dispose();
        recordBase = null!;
        customers = null;
        orders = null;
        profiles = null;
        notes = null;
        hookDocuments = null;
        graphCustomers = null;
        graphOrders = null;
        graphLines = null;
        pathGraphs = null;
    }

    /// <summary>Deletes only the constructor-normalized dedicated laboratory directory after verifying it is not a filesystem root.<br/></summary>
    private static void DeleteOwnedDirectory(string directory)
    {
        string root = Path.GetPathRoot(directory) ?? "";
        if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The showcase laboratory cannot be a filesystem root.");
        if (!Directory.Exists(directory)) return;
        string marker = Path.Combine(directory, OwnershipFileName);
        if (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipContract)
            throw new InvalidOperationException("The laboratory directory is not owned by Abraxas Showcase and will not be replaced.");
        Directory.Delete(directory, true);
    }

    /// <summary>
    /// Creates the dedicated directory when absent and requires its ownership marker before using a non-empty existing directory.<br/>
    /// This prevents a custom command-line data path from turning regeneration into broad deletion of unrelated files.<br/>
    /// </summary>
    private void EnsureOwnedDirectory()
    {
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, OwnershipFileName);
        bool hasEntries = Directory.EnumerateFileSystemEntries(directory).Any();
        if (hasEntries && (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipContract))
            throw new InvalidOperationException("Choose an empty directory or an existing Abraxas Showcase laboratory.");
        if (!File.Exists(marker)) File.WriteAllText(marker, OwnershipContract);
    }

    /// <summary>Rejects unreasonable or ambiguous generation values before any existing laboratory is replaced.<br/></summary>
    /// <param name="request">Candidate generation request from the local packaged UI.<br/></param>
    private static void Validate(GenerationRequest request)
    {
        if (request.CustomerCount is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(request.CustomerCount));
        if (request.OrdersPerCustomer is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(request.OrdersPerCustomer));
        if (request.Complexity is not ("Simple" or "Relational" or "Dense")) throw new ArgumentException("Choose Simple, Relational, or Dense complexity.");
        _ = checked(request.CustomerCount * request.OrdersPerCustomer);
    }

   /// <summary>Measures only files below the dedicated laboratory directory and excludes no hidden storage costs.<br/></summary>
    /// <returns>Total current file length in bytes.<br/></returns>
    private long DirectoryBytes() => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
        : 0;

    /// <summary>Checks one active durable store without creating or opening a new store.<br/></summary>
    /// <param name="name">Stable store name owned by the active scenario group.<br/></param>
    /// <returns><see langword="true"/> when the store is active.<br/></returns>
    internal bool HasStore(string name) => recordBase?.StoreExists(name) == true;

   /// <summary>Hydrates a bounded natural-order sample from one real typed RecordStore.<br/></summary>
    /// <typeparam name="T">Stored domain type hydrated by the reader.<br/></typeparam>
    /// <param name="name">Durable store name shown by the data browser.<br/></param>
    /// <param name="store">Open typed store owned by the active group.<br/></param>
    /// <param name="sampleLimit">Maximum sample count retained for exploration.<br/></param>
    /// <returns>Store metadata, total count, and the bounded hydrated sample.<br/></returns>
    private void WritePresenceMetadata(LaboratoryManifest manifest)
    {
        string metadataPath = Path.Combine(directory, PresenceMetadataFileName);
        string json = JsonSerializer.Serialize(new { groupId = manifest.GroupID, setupVersion = manifest.SetupVersion });
        File.WriteAllText(metadataPath, json);
    }

    /// <summary>Reports whether the open RecordBase belongs to the identified group regardless of its completion state.<br/></summary>
    /// <param name="group">Registered group expected to own the active environment.<br/></param>
    /// <returns><see langword="true"/> when the persisted group identifier matches.<br/></returns>
    internal bool IsGroupActive(ScenarioGroupDefinition group) => manifests.Get(manifestID).GroupID == group.Id;

    /// <summary>Returns the developer-facing title of the group owning the open RecordBase.<br/></summary>
    internal string ActiveGroupTitle
    {
        get
        {
            string groupID = manifests.Get(manifestID).GroupID;
            return ScenarioGroupCatalog.All.FirstOrDefault(group => group.Id == groupID)?.Title ?? "current group";
        }
    }

    /// <summary>
    /// Closes the active RecordBase, nulls every typed-store reference, and deletes the exact Showcase-owned directory.<br/>
    /// Ownership verification remains the final guard before any files are removed.<br/>
    /// </summary>
    internal void DeleteEnvironment()
    {
        Close();
        DeleteOwnedDirectory(directory);
    }

    /// <summary>Finds the newest physical write time among files belonging to the named Showcase RecordBase.<br/></summary>
    /// <returns>The newest UTC write time, or <see langword="null"/> when the RecordBase has no files.<br/></returns>
    private DateTime? RecordBaseLastModifiedUtc()
    {
        if (!Directory.Exists(directory)) return null;
        string[] files = Directory.GetFiles(directory, RecordBaseName + ".*", SearchOption.TopDirectoryOnly);
        return files.Length == 0 ? null : files.Max(File.GetLastWriteTimeUtc);
    }
}
