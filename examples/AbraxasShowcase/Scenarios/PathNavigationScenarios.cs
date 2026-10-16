using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Progressive direct-value lessons over one graph containing a derived collection, shared member, literal keys, and a multidimensional array.<br/></summary>
internal static class PathNavigationScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("path-collection-position", "Select an element inside a stored collection", "Read an address street through #0 without hydrating the containing graph.", 522, "Projection/CollectionPosition.cs.txt", CollectionPosition),
        New("path-derived-collection-member", "Follow a member on a collection subclass", "Read Home and #0 directly, then hydrate the AddressBook to verify its own member and inherited list entries survive together.", 523, "Projection/DerivedCollectionMember.cs.txt", DerivedCollectionMember),
        New("path-literal-dictionary-keys", "Read literal dictionary keys", "Read keys containing spaces and path-like punctuation without escaping them.", 524, "Projection/LiteralDictionaryKeys.cs.txt", LiteralDictionaryKeys),
        New("path-multidimensional-coordinate", "Read a multidimensional coordinate", "Select one coordinate value with @1,2 without building the array or root graph.", 527, "Projection/MultidimensionalCoordinate.cs.txt", MultidimensionalCoordinate)
    ];

    /// <summary>Constructs one independent runnable navigation lesson using its exact learner-facing fragment.<br/></summary>
    /// <param name="id">Stable navigation identity.<br/></param>
    /// <param name="title">Developer-facing scenario title.<br/></param>
    /// <param name="description">One-sentence outcome.<br/></param>
    /// <param name="order">Learning path order after simple value reads.<br/></param>
    /// <param name="source">Embedded exact C# fragment.<br/></param>
    /// <param name="run">Compiled action over the prepared group store.<br/></param>
    /// <returns>The runnable catalog scenario.<br/></returns>
    private static IShowcaseScenario New(string id, string title, string description, int order, string source,
        Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run) =>
        new CatalogActionScenario(id, "Projection and value reads", title, description, "One direct graph value read", false,
            new ScenarioDocumentation(description, ["PropPath navigation", "selected value without root hydration"], ["GetValue<T>", "RecordID"],
                [title, description], ["Read paths without a result type"], order), source, run);

    /// <summary>Returns the setup-owned path-graph identity without hydrating its object.<br/></summary>
    /// <param name="workspace">Active projection group with one deterministic graph record.<br/></param>
    /// <returns>RecordID of the first prepared path graph.<br/></returns>
    private static ulong GraphID(ShowcaseWorkspace workspace)
    {
        using var reader = workspace.PathGraphs.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The projection group's path graph was not prepared.");
        return reader.ID;
    }

    /// <summary>Reads one list element's member directly from the persisted graph identity.<br/></summary>
    /// <param name="workspace">Prepared projection group.<br/></param>
    /// <param name="token">Cancellation observed before reading.<br/></param>
    /// <returns>The street selected by the #0 path.<br/></returns>
    private static ScenarioResult CollectionPosition(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ulong id = GraphID(workspace);
        var watch = Stopwatch.StartNew();
        string street = workspace.PathGraphs.GetValue<string>(id, ".Addresses#0.Street");
        watch.Stop();
        return new ScenarioResult(street == "100 Home Lane", "#0 selected an address member inside a stored collection subclass without hydrating the root graph.",
            [new("PropPath", ".Addresses#0.Street")], street, typeof(string), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Compares direct paths through a list element and an ordinary member, then checks whole-object hydration retains both and their shared identity.<br/></summary>
    /// <param name="workspace">Prepared projection group.<br/></param>
    /// <param name="token">Cancellation observed before reading.<br/></param>
    /// <returns>Two direct values plus the hydrated address collection, with a proof that its Home member is element zero.<br/></returns>
    private static ScenarioResult DerivedCollectionMember(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ulong id = GraphID(workspace);
        var watch = Stopwatch.StartNew();
        string byPosition = workspace.PathGraphs.GetValue<string>(id, ".Addresses#0.Street");
        string byMember = workspace.PathGraphs.GetValue<string>(id, ".Addresses.Home.Street");
        ShowcasePathGraph? hydrated = workspace.PathGraphs.Get(id);
        ShowcaseAddressBook? addresses = hydrated?.Addresses;
        watch.Stop();
        bool passed = byPosition == "100 Home Lane" && byMember == byPosition
            && addresses is { Count: 3, Home: not null }
            && addresses.Select(address => address.Street).SequenceEqual(["100 Home Lane", "200 Work Way", "300 Archive Road"])
            && ReferenceEquals(addresses.Home, addresses[0]);
        return new ScenarioResult(passed, "Direct paths and whole-object hydration preserved the list entries, Home member, and their shared address.",
            [new("Position path", ".Addresses#0.Street"), new("Member path", ".Addresses.Home.Street"), new("Hydrated list entries", addresses?.Count.ToString() ?? "Missing")],
            [new ScenarioResultItem("By position", id, byPosition, typeof(string)), new ScenarioResultItem("By Home member", id, byMember, typeof(string)), new ScenarioResultItem("Hydrated AddressBook", id, addresses, typeof(ShowcaseAddressBook))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads literal dictionary keys containing spaces and structural-looking punctuation from two address routes.<br/></summary>
    /// <param name="workspace">Prepared projection group.<br/></param>
    /// <param name="token">Cancellation observed before reading.<br/></param>
    /// <returns>Both note values and a string-length continuation proof.<br/></returns>
    private static ScenarioResult LiteralDictionaryKeys(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ulong id = GraphID(workspace);
        var watch = Stopwatch.StartNew();
        string gate = workspace.PathGraphs.GetValue<string>(id, ".Addresses.Home.Notes!Gate Code");
        string important = workspace.PathGraphs.GetValue<string>(id, ".Addresses#2.Notes!Important!");
        int length = workspace.PathGraphs.GetValue<int>(id, ".Addresses#2.Notes!Important!.Length");
        watch.Stop();
        bool passed = gate == "2468" && important == "Keep this" && length == important.Length;
        return new ScenarioResult(passed, "Literal note keys and a value continuation resolved without escaping their punctuation.",
            [new("Space-containing key", ".Addresses.Home.Notes!Gate Code"), new("Punctuation key", ".Addresses#2.Notes!Important!.Length")],
            [new ScenarioResultItem("Gate Code", id, gate, typeof(string)), new ScenarioResultItem("Important! length", id, length, typeof(int))], watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads one member of a rank-two array element by row and column without hydrating the graph.<br/></summary>
    /// <param name="workspace">Prepared projection group.<br/></param>
    /// <param name="token">Cancellation observed before reading.<br/></param>
    /// <returns>The value at coordinate 1,2.<br/></returns>
    private static ScenarioResult MultidimensionalCoordinate(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ulong id = GraphID(workspace);
        var watch = Stopwatch.StartNew();
        decimal latitude = workspace.PathGraphs.GetValue<decimal>(id, ".Coordinates@1,2.Latitude");
        watch.Stop();
        return new ScenarioResult(latitude == 12m, "@1,2 selected one rank-two array element and its Latitude member.",
            [new("PropPath", ".Coordinates@1,2.Latitude")], latitude, typeof(decimal), watch.Elapsed.TotalMilliseconds, id);
    }
}
