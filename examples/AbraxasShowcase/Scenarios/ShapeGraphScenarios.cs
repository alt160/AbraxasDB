using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Orders the Inheto shape-control workup before the two Abraxas cross-store link patterns.<br/></summary>
internal static class ShapeGraphScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("contextual-type-codec", "Shape and materialization", "Choose a versioned write format", "Store a regex as pattern and options; ambient app context chooses the byte format, and a later read decodes after that context changes.", 525, "Hooks/ContextualCodec.cs.txt", ContextualCodec, true),
        New("exclude-member-path", "Shape and materialization", "Exclude one member path", "Leave PrivateMemo out of each stored document.", 530, "Hooks/ExcludeMember.cs.txt", ExcludeMember),
        New("exclude-member-type", "Shape and materialization", "Exclude a member type", "Leave every ShowcaseHookTransient value out of this store's writes.", 540, "Hooks/ExcludeType.cs.txt", ExcludeType),
        New("custom-type-codec", "Shape and materialization", "Use a type codec", "Need the same conversion wherever a type appears? Apply one writer and reader to every TypeTokens element.", 550, "Hooks/TypeCodec.cs.txt", TypeCodec),
        New("custom-property-codec", "Shape and materialization", "Use a property-path codec", "Need one member stored differently? Supply a writer and reader for PathToken without changing other tokens.", 560, "Hooks/PropertyCodec.cs.txt", PropertyCodec),
        New("custom-object-activator", "Shape and materialization", "Activate a nested object", "Need control over construction? Create Address with your own activator before Abraxas fills its stored members.", 570, "Hooks/Activator.cs.txt", Activator),
        New("hydrate-packed-lines", "Object graphs across stores", "Read ordered lines from IDs", "Read an order and follow its line RecordIDs to objects held in a separate store.", 580, "Graphs/HydrateLines.cs.txt", HydrateLines),
        New("hydrate-reverse-orders", "Object graphs across stores", "Read orders from a customer", "Use a customer's RecordID to find orders whose indexed CustomerID matches it.", 590, "Graphs/HydrateOrders.cs.txt", HydrateOrders),
        New("read-order-without-links", "Object graphs across stores", "Read an order without links", "Need only the order's own values? Skip building its related line objects.", 600, "Graphs/ReadNoLinks.cs.txt", ReadNoLinks)
    ];

    /// <summary>Builds a runnable, searchable entry whose shown source contains only the related Abraxas operation.<br/></summary>
    /// <param name="id">Stable scenario identifier.<br/></param>
    /// <param name="category">Owning group title.<br/></param>
    /// <param name="title">Learner-facing scenario title.<br/></param>
    /// <param name="description">Short intent and observable behavior.<br/></param>
    /// <param name="order">Curriculum order after shape projection and before general relationships.<br/></param>
    /// <param name="source">Embedded paste-ready scenario fragment.<br/></param>
    /// <param name="run">Native scenario action over the active group.<br/></param>
    /// <param name="mutates">Whether the scenario writes an additional example record.<br/></param>
    /// <returns>A complete compiled scenario contract.<br/></returns>
    private static IShowcaseScenario New(string id, string category, string title, string description, int order, string source,
        Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run, bool mutates = false) =>
        new CatalogActionScenario(id, category, title, description, mutates ? "Single-record write and read" : "Single-record read", mutates,
            new ScenarioDocumentation(description, [title], [category == "Shape and materialization" ? "SerializationOptions" : "Links"],
                [title, description], ["Prepare " + category], order), source, run);

    /// <summary>Returns a real hydrated document and proves the excluded member path has no stored value.<br/></summary>
    private static ScenarioResult ExcludeMember(ShowcaseWorkspace workspace, CancellationToken token) => ReadDocument(workspace, token,
        document => document.PrivateMemo == "", "PrivateMemo was absent after the stored document was read.");

    /// <summary>Returns a real hydrated document and proves its excluded member type was not persisted.<br/></summary>
    private static ScenarioResult ExcludeType(ShowcaseWorkspace workspace, CancellationToken token) => ReadDocument(workspace, token,
        document => document.Transient is null, "The Transient member type was excluded from storage.");

    /// <summary>Shows the path-specific token hook round-tripping the original semantic code.<br/></summary>
    private static ScenarioResult PropertyCodec(ShowcaseWorkspace workspace, CancellationToken token) => ReadDocument(workspace, token,
        document => document.PathToken.Code > 0, "The property-path codec restored PathToken.Code.");

    /// <summary>Shows the type hook round-tripping each token in the requested document's collection.<br/></summary>
    private static ScenarioResult TypeCodec(ShowcaseWorkspace workspace, CancellationToken token) => ReadDocument(workspace, token,
        document => document.TypeTokens.Length > 0 && document.TypeTokens.All(value => value.Code > 0), "The type codec restored every TypeTokens value.");

    /// <summary>Shows custom construction of Address before its stored City value was populated.<br/></summary>
    private static ScenarioResult Activator(ShowcaseWorkspace workspace, CancellationToken token) => ReadDocument(workspace, token,
        document => document.Address.CreatedBy == "Custom activator" && document.Address.City.Length > 0,
        "The activator created Address; Inheto then filled its stored City.");

    /// <summary>Reads one actual prepared document and evaluates one independent materialization assertion.<br/></summary>
    /// <param name="workspace">Prepared hook-group store.<br/></param>
    /// <param name="token">Cancellation before the materializing read.<br/></param>
    /// <param name="passes">Assertion over the real hydrated document.<br/></param>
    /// <param name="summary">Human-readable proof conclusion.<br/></param>
    /// <returns>The retained document for independent live exploration.<br/></returns>
    private static ScenarioResult ReadDocument(ShowcaseWorkspace workspace, CancellationToken token, Func<ShowcaseHookDocument, bool> passes, string summary)
    {
        using var reader = workspace.HookDocuments.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The hook group has no documents.");
        ulong id = reader.ID;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseHookDocument document = workspace.HookDocuments.Get(id)!;
        watch.Stop();
        return new ScenarioResult(passes(document), summary, [new("RecordID", id.ToString())], document, typeof(ShowcaseHookDocument), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Proves ordinary order materialization follows stored line identities to authoritative child payloads.<br/></summary>
    private static ScenarioResult HydrateLines(ShowcaseWorkspace workspace, CancellationToken token)
    {
        using var reader = workspace.GraphOrders.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The graph group has no orders.");
        ulong id = reader.ID;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseGraphOrder order = workspace.GraphOrders.Get(id)!;
        watch.Stop();
        bool passed = order.Lines?.Count == 2 && order.Lines.All(line => line.ID != 0 && workspace.GraphLines.Get(line.ID)?.Sku == line.Sku) == true;
        return new ScenarioResult(passed, "Get returned an order with two separately stored lines in their original order.",
            [new("Line store records", workspace.GraphLines.Aggregates.Count().ToString())], order, typeof(ShowcaseGraphOrder), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Proves the parent RecordID resolves children through the prepared indexed CustomerID path.<br/></summary>
    private static ScenarioResult HydrateOrders(ShowcaseWorkspace workspace, CancellationToken token)
    {
        using var reader = workspace.GraphCustomers.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The graph group has no customers.");
        ulong id = reader.ID;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseGraphCustomer customer = workspace.GraphCustomers.Get(id)!;
        watch.Stop();
        bool passed = customer.Orders?.Count > 0 && customer.Orders.All(order => order.CustomerID == id && order.Lines?.Count == 2) == true;
        return new ScenarioResult(passed, "Get populated Orders through the indexed child relationship, including each order's lines.",
            [new("Related orders", customer.Orders?.Count.ToString() ?? "0")], customer, typeof(ShowcaseGraphCustomer), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Contrasts a links-suppressed scalar read with normal graph hydration for the same RecordID.<br/></summary>
    private static ScenarioResult ReadNoLinks(ShowcaseWorkspace workspace, CancellationToken token)
    {
        using var reader = workspace.GraphOrders.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The graph group has no orders.");
        ulong id = reader.ID;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ShowcaseGraphOrder scalar = reader.GetNoLinks();
        watch.Stop();
        return new ScenarioResult(scalar.Lines is null, "GetNoLinks returned the order's own values without building its Lines.",
            [new("RecordID", id.ToString())], scalar, typeof(ShowcaseGraphOrder), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Writes under one ambient format preference and reads under another to prove the persisted marker controls decoding.<br/></summary>
    /// <param name="workspace">Prepared shape group whose document store has the contextual type hooks registered.<br/></param>
    /// <param name="token">Cancellation observed before the scenario-owned write.<br/></param>
    /// <returns>The actual restored document and evidence that the read did not require the write-time ambient value.<br/></returns>
    private static ScenarioResult ContextualCodec(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool previous = ShowcaseCodecContext.UseUtf16.Value;
        var watch = Stopwatch.StartNew();
        try
        {
            ShowcaseCodecContext.UseUtf16.Value = true;
            ulong id = workspace.HookDocuments.Put(new ShowcaseHookDocument
            {
                Title = "Context-selected wire format",
                Address = new ShowcaseHookAddress { City = "Phoenix" },
                ContextPayload = new ShowcaseContextualPayload
                {
                    Text = "Read after context changes",
                    Pattern = new System.Text.RegularExpressions.Regex("^read after", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                }
            });
            ShowcaseCodecContext.UseUtf16.Value = false;
            ShowcaseHookDocument restored = workspace.HookDocuments.Get(id)!;
            watch.Stop();
            bool passed = restored.ContextPayload.Text == "Read after context changes"
                && restored.ContextPayload.Pattern.IsMatch(restored.ContextPayload.Text)
                && restored.ContextPayload.Pattern.Options.HasFlag(System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return new ScenarioResult(passed, passed ? "The type hook decoded the stored UTF-16 marker after the ambient setting changed to UTF-8." : "The context-independent read did not recover the payload.",
                [new("Write setting", "UTF-16"), new("Read setting", "UTF-8"), new("Stored marker", "2 = UTF-16")],
                restored, typeof(ShowcaseHookDocument), watch.Elapsed.TotalMilliseconds, id);
        }
        finally
        {
            ShowcaseCodecContext.UseUtf16.Value = previous;
        }
    }
}
