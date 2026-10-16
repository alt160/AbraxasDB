using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Small self-contained environment examples that introduce Abraxas index shapes before their deeper query examples.<br/></summary>
internal static class FeatureFoundationScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("index-method-path", "Index a method-continuation path", "Find records by a value reached through a member and a parameterless method, using a path chosen as text.", 34, "Fundamentals/IndexMethodPath.cs.txt", IndexMethodPath),
        New("index-calculated-key", "Index a calculated value", "Find records by a key made from Region and Code without adding that combined value to the object.", 35, "Fundamentals/IndexCalculatedKey.cs.txt", IndexCalculatedKey),
        New("index-composite-key", "Create a composite index", "Find names by their separate parts, then read a display name from the index keys.", 36, "Fundamentals/IndexCompositeKey.cs.txt", IndexCompositeKey),
        New("index-text-projections", "Create text search projections", "See how case-insensitive and suffix searches use extra text keys, separate from ascending or descending order.", 37, "Fundamentals/IndexTextProjections.cs.txt", IndexTextProjections),
        New("index-memory-only", "Create a memory-only index", "Build a temporary lookup that disappears on close while the underlying stored records remain.", 38, "Fundamentals/IndexMemoryOnly.cs.txt", IndexMemoryOnly),
        New("analyze-foundation-values", "Analyze values with a custom struct", "Read three values per order into your own struct instead of building a full order object.", 39, "Fundamentals/AnalyzeValues.cs.txt", AnalyzeValues),
        New("rebuild-index-without-records", "Rebuild an index without rewriting records", "Remove and recreate a durable index, then reopen the RecordBase and retrieve the same record by ID.", 40, "Fundamentals/RebuildIndex.cs.txt", RebuildIndexWithoutRecords),
        New("unique-scalar-index", "Require a unique account number", "Reject a second record with the same numeric key and keep the original record intact.", 31, "Fundamentals/UniqueScalarIndex.cs.txt", UniqueScalarIndex),
        New("unique-string-index", "Require a unique login", "Reject an exact duplicate string key and verify the original login still resolves after reopening.", 32, "Fundamentals/UniqueStringIndex.cs.txt", UniqueStringIndex),
        New("unique-composite-index", "Require a unique tenant and login pair", "Allow the same login in different tenants while rejecting a duplicate complete pair.", 33, "Fundamentals/UniqueCompositeIndex.cs.txt", UniqueCompositeIndex)
    ];

    /// <summary>Creates one independently runnable foundations lesson using the shared scenario contract and paste-ready source.<br/></summary>
    /// <param name="id">Stable scenario identity.<br/></param>
    /// <param name="title">Short learner-facing title.<br/></param>
    /// <param name="description">One-sentence outcome.<br/></param>
    /// <param name="order">Position in the learning path.<br/></param>
    /// <param name="source">Exact embedded C# example path.<br/></param>
    /// <param name="run">Native action that executes against the scenario-owned RecordBase.<br/></param>
    /// <returns>A compiled runnable scenario with searchable documentation.<br/></returns>
    private static IShowcaseScenario New(string id, string title, string description, int order, string source, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run)
    {
        (string explanation, string[] concepts, string[] apiNames) detail = id switch
        {
            "index-method-path" => ("Need to look up a value reached through a parameterless method? CreateIndex accepts an admitted property path as text, even when your configuration chooses that path at runtime. It does not accept an arbitrary C# function here; a fixed path can also be written as a lambda.", ["property path", "method continuation", "explicit index"], ["PropPath", "CreateIndex", "Where.PropPath"]),
            "index-calculated-key" => ("If you would normally add a combined lookup field, try a calculated index instead. This example derives a key from Region and Code, then searches with the same calculation. Later writes maintain the index.", ["calculated selector", "durable expression index"], ["Calc.Concat", "CreateExpressionIndex", "Where.Calc"]),
            "index-composite-key" => ("Want to index a name built from two properties? Create one named composite index over FirstName and LastName, not two property indexes or one concatenated string. Its key keeps the parts separate: search with a prefix on one and an exact value on the other, then use a typed, index-only reader to obtain both values without opening each customer object. Borrow UTF-8 spans when you do not need managed strings.", ["composite parts", "prefix plus exact", "typed index-only read"], ["CreateCompositeIndex", "StartsWith", "OpenCompositeValues", "GetUtf8Span"]),
            "index-text-projections" => ("Want case-insensitive or suffix search? Abraxas can maintain extra versions of a text key for those lookups. A reversed-key version helps suffix matching; it does not mean the index is physically sorted descending.", ["folded text", "reversed-key suffix", "physical sort order"], ["CreateIndex", "TextIndexProfile", "EndsWith"]),
            "index-memory-only" => ("Need a lookup for this run but not after reopen? A memory-only index disappears when the store closes. The indexed value can still be in the durable record, so this is not by itself a privacy control.", ["volatile index", "index lifecycle", "durable payload caveat"], ["CreateMemoryIndex", "GetIndexes"]),
            "analyze-foundation-values" => ("For a calculation across many orders, read just the values you need and put them in your own struct. This avoids building a ShowcaseOrder in application code; the later performance examples measure actual allocations rather than promising zero.", ["selected values", "custom struct", "record analysis"], ["OpenValuesReader", "ValueAccessor", "Read"]),
            "rebuild-index-without-records" => ("A RecordID read uses the stored record, not a secondary search index. Remove the Code index and the same ID still retrieves the customer. Recreate the index from intact records, then reopen and use Code lookup again. This does not claim recovery from damage to authoritative record data.", ["record versus index", "index rebuild", "durable reopen"], ["Get", "RemoveIndex", "CreateIndex", "GetOrDefault"]),
            "unique-scalar-index" => ("Set unique: true on an ordinary numeric index when no two records may have the same account number. A duplicate Put raises a unique-constraint error; it does not replace the first account. The index remains available after reopening.", ["unique index", "numeric scalar", "duplicate insert"], ["CreateIndex", "Put", "IndexContractException", "GetOrDefault"]),
            "unique-string-index" => ("The same unique: true option works for a string property such as Login. This example enforces exact stored string keys; do not assume it also rejects case variants. A folded text index is a separate choice when case-insensitive uniqueness is required.", ["unique index", "exact string key", "durable reopen"], ["CreateIndex", "Put", "IndexContractException", "GetOrDefault"]),
            "unique-composite-index" => ("A composite unique index treats Tenant and Login as one two-part key. Two accounts may share the same Login when their Tenant values differ; a second account with both parts equal is rejected. Neither property needs its own unique index. This example uses exact string parts and verifies accepted records after reopen.", ["unique composite key", "complete pair", "shared component"], ["CreateCompositeIndex", "CompositeIndexOptions.Unique", "Put", "IndexContractException"]),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        return new CatalogActionScenario(id, "Recordbase Fundamentals", title, description, id == "analyze-foundation-values" ? "Small traversal" : "Small index build", true,
            new ScenarioDocumentation(detail.explanation,
                detail.concepts, detail.apiNames, [title, description], ["Create and reuse RecordStore indexes"], order), source, run);
    }

    /// <summary>Builds an index for a derived member path and proves that it can select an existing record.<br/></summary>
    /// <param name="workspace">Owner of the fundamentals run directory.<br/></param>
    /// <param name="token">Cancellation signal before file mutation.<br/></param>
    /// <returns>Selection proof and the exact indexed path.<br/></returns>
    private static ScenarioResult IndexMethodPath(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
        ulong id = customers.Put(new ShowcaseCustomer { Code = " PATH-1 ", Name = "Path example", Region = "West", Tags = ["priority"] });
        string indexPath = ".Code.Trim().Length";
        customers.CreateIndex(new PropPath(indexPath), IndexDirection.Ascending);
        ulong[] matches = customers.IterateRecordIDs(customers.Where.PropPath(indexPath).AsInt32.EqualTo(6).EndCondition).ToArray();
        watch.Stop();
        bool passed = matches.SequenceEqual([id]);
        return new ScenarioResult(passed, passed ? "A derived member path selected the stored customer." : "The indexed path did not select the expected customer.",
            [new("Property path", indexPath), new("Matches", matches.Length.ToString())], passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Registers a durable multi-member calculated selector and proves its expression can select the inserted record.<br/></summary>
    /// <param name="workspace">Owner of the fundamentals run directory.<br/></param>
    /// <param name="token">Cancellation signal before file mutation.<br/></param>
    /// <returns>Expression-index selection proof.<br/></returns>
    private static ScenarioResult IndexCalculatedKey(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
        ulong id = customers.Put(new ShowcaseCustomer { Code = "CALC-1", Name = "Calculated example", Region = "West" });
        var display = customers.Calc.Concat("{.Region}|{.Code}");
        customers.CreateExpressionIndex(display.ForIndexing(tokenSource: "region-code"));
        ulong[] matches = customers.IterateRecordIDs(customers.Where.Calc(display).EqualTo("West|CALC-1").EndCondition).ToArray();
        watch.Stop();
        bool passed = matches.SequenceEqual([id]);
        return new ScenarioResult(passed, passed ? "The calculated key selected the stored customer." : "The calculated key did not select the expected customer.",
            [new("Calculated key", "West|CALC-1"), new("Matches", matches.Length.ToString())], passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Creates a durable name index, verifies prefix-plus-exact routed selection, and projects display names from covered key parts.<br/></summary>
    /// <param name="workspace">Owner of the fundamentals run directory.<br/></param>
    /// <param name="token">Cancellation signal before file mutation.<br/></param>
    /// <returns>Composite-key selection proof.<br/></returns>
    private static ScenarioResult IndexCompositeKey(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
        ulong id = customers.Put(new ShowcaseCustomer { Code = "COMP-1", FirstName = "Ada", LastName = "Lovelace" });
        customers.Put(new ShowcaseCustomer { Code = "COMP-2", FirstName = "Ada", LastName = "Byron" });
        var selector = new Selector<ShowcaseCustomer>();
        customers.CreateCompositeIndex("display-name", selector.Prop(customer => customer.FirstName).ForIndexing(), selector.Prop(customer => customer.LastName).ForIndexing());
        var condition = customers.Where.Prop(customer => customer.FirstName).StartsWith("Ad").AND.Prop(customer => customer.LastName).EqualTo("Lovelace").EndCondition;
        ConditionExecutionPlanEventArgs? plan = null;
        customers.ConditionExecutionPlanned += (_, args) => plan = args;
        ulong[] matches = customers.IterateRecordIDs(condition).ToArray();
        watch.Stop();
        bool routed = plan?.PlanKind == ConditionExecutionPlanKind.IndexCandidateWithResidual
            && plan.IndexNames.Contains("display-name", StringComparer.Ordinal);
        var displayNames = new Dictionary<ulong, string>();
        using (var names = customers.Readers.OpenCompositeValues<string, string>("display-name"))
            while (names.Read())
                displayNames[names.ID] = $"{names.Value1} {names.Value2}";
        bool passed = matches.SequenceEqual([id]) && routed && displayNames.TryGetValue(id, out string? displayName) && displayName == "Ada Lovelace";
        return new ScenarioResult(passed, passed ? "The composite index selected the customer and supplied both display-name values." : "The composite condition, routed plan, or covered value read did not match the expected result.",
            [new("Index", "display-name"), new("Ordered parts", "FirstName, LastName"), new("Condition", "FirstName starts with Ad AND LastName equals Lovelace"), new("Covered display name", displayNames.GetValueOrDefault(id, "(missing)")), new("Routed candidate plan", routed.ToString()), new("Matches", matches.Length.ToString())], passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Shows folded and reversed text projections and checks a case-insensitive suffix condition.<br/></summary>
    /// <param name="workspace">Owner of the fundamentals run directory.<br/></param>
    /// <param name="token">Cancellation signal before file mutation.<br/></param>
    /// <returns>Text-subindex selection proof.<br/></returns>
    private static ScenarioResult IndexTextProjections(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
        ulong id = customers.Put(new ShowcaseCustomer { Code = "TEXT-1", Name = "Taylor Example", Region = "West" });
        customers.CreateIndex(customer => customer.Name, TextIndexProfile.ExactAndFolded, IndexDirection.Bidirectional);
        ulong[] matches = customers.IterateRecordIDs(customers.Where.PropPath(".Name").AsString.EndsWith("EXAMPLE", ignoreCase: true).EndCondition).ToArray();
        watch.Stop();
        bool passed = matches.SequenceEqual([id]);
        return new ScenarioResult(passed, passed ? "Folded reversed-key lookup found the suffix." : "The suffix condition did not select the expected customer.",
            [new("Text representations", "Exact, folded"), new("Key projections", "Forward and reversed"), new("Physical order", "Ascending"), new("Matches", matches.Length.ToString())], passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Uses a volatile index for one session without presenting it as protection for the underlying durable record.<br/></summary>
    /// <param name="workspace">Owner of the fundamentals run directory.<br/></param>
    /// <param name="token">Cancellation signal before file mutation.<br/></param>
    /// <returns>Volatile-index selection proof.<br/></returns>
    private static ScenarioResult IndexMemoryOnly(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        bool volatileVisible;
        bool selected;
        using (var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
            ulong id = customers.Put(new ShowcaseCustomer { Code = "MEM-1", Name = "Transient example", Region = "West" });
            customers.CreateMemoryIndex(customer => customer.Name, IndexDirection.Ascending);
            ulong[] matches = customers.IterateRecordIDs(customers.Where.PropPath(".Name").AsString.EqualTo("Transient example").EndCondition).ToArray();
            volatileVisible = customers.GetIndexes(useMemoryIndex: true).Any(index => index.Backend.KeyPath == ".Name");
            selected = matches.SequenceEqual([id]);
        }
        bool absentAfterReopen;
        using (var reopened = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var customers = reopened.OpenStore<ShowcaseCustomer>("customers");
            absentAfterReopen = !customers.GetIndexes(useMemoryIndex: true).Any(index => index.Backend.KeyPath == ".Name");
        }
        watch.Stop();
        bool passed = volatileVisible && selected && absentAfterReopen;
        return new ScenarioResult(passed, passed ? "The index selected a record, then disappeared after the RecordBase reopened." : "The memory-only index lifecycle did not match expectations.",
            [new("Available this session", volatileVisible.ToString()), new("Absent after reopen", absentAfterReopen.ToString()), new("Underlying record", "Still durable")], passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Walks a small order store and fills application-owned struct state from three typed value paths.<br/></summary>
    /// <param name="workspace">Owner of this isolated Fundamentals RecordBase.<br/></param>
    /// <param name="token">Cancellation signal observed between rows.<br/></param>
    /// <returns>Count and checksum from the user-defined struct calculation.<br/></returns>
    private static ScenarioResult AnalyzeValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase");
        var orders = recordBase.CreateOrOpenStore<ShowcaseOrder>("orders", "Analysis orders");
        orders.Put(new ShowcaseOrder { Number = "A-1", CustomerID = 5, OrderedAt = new DateTime(2026, 1, 2), Total = 42.5m });
        orders.Put(new ShowcaseOrder { Number = "A-2", CustomerID = 6, OrderedAt = new DateTime(2026, 2, 3), Total = 17.25m });
        decimal score = 0m;
        int rows = 0;
        using (var values = orders.OpenValuesReader(".Total", ".OrderedAt", ".CustomerID"))
            while (values.Read())
            {
                token.ThrowIfCancellationRequested();
                var row = new ShowcaseOrderAnalysisRow(values.Get(0).AsDecimal ?? 0m,
                    values.Get(1).AsDateTime ?? default, values.Get(2).AsUInt64 ?? 0UL);
                score += row.Score();
                rows++;
            }
        watch.Stop();
        return new ScenarioResult(rows == 2, "Two stored orders supplied three typed fields each to one custom analysis struct.",
            [new("Orders visited", rows.ToString()), new("Fields per row", "3"), new("Analysis score", score.ToString("0.00"))],
            score, typeof(decimal), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Proves a secondary index can be removed and rebuilt from intact records without changing their storage identity.<br/></summary>
    /// <param name="workspace">Owner of this isolated Fundamentals RecordBase.<br/></param>
    /// <param name="token">Cancellation signal observed before each storage phase.<br/></param>
    /// <returns>Proof that direct identity reads survive index removal and indexed lookup works after rebuild and reopen.<br/></returns>
    private static ScenarioResult RebuildIndexWithoutRecords(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong id;
        bool survivedRemoval;
        using (var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Showcase customers");
            id = customers.Put(new ShowcaseCustomer { Code = "REBUILD-1", Name = "Index lifecycle", Region = "West" });
            customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
            if (!customers.RemoveIndex(".Code"))
                throw new InvalidOperationException("The Code index could not be removed.");
            survivedRemoval = customers.Get(id)?.Code == "REBUILD-1";
            token.ThrowIfCancellationRequested();
            customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
        }
        token.ThrowIfCancellationRequested();
        bool foundAfterReopen;
        bool sameRecordAfterReopen;
        using (var reopened = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var customers = reopened.OpenStore<ShowcaseCustomer>("customers");
            sameRecordAfterReopen = customers.Get(id)?.Code == "REBUILD-1";
            foundAfterReopen = customers.GetOrDefault(".Code", "REBUILD-1")?.Name == "Index lifecycle";
        }
        watch.Stop();
        bool passed = survivedRemoval && sameRecordAfterReopen && foundAfterReopen;
        return new ScenarioResult(passed, passed ? "The index was rebuilt from intact records; the RecordID and customer survived removal and reopen." : "Index independence or rebuilt lookup did not match expectations.",
            [new("Direct ID read after removal", survivedRemoval.ToString()), new("Same ID after reopen", sameRecordAfterReopen.ToString()), new("Code lookup after rebuild", foundAfterReopen.ToString())],
            passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Creates a durable unique numeric index and proves a duplicate insert leaves the original account intact.<br/></summary>
    /// <param name="workspace">Owner of this isolated Fundamentals RecordBase.<br/></param>
    /// <param name="token">Cancellation signal checked before each storage phase.<br/></param>
    /// <returns>Duplicate-rejection and durable-lookup proof for the numeric key.<br/></returns>
    private static ScenarioResult UniqueScalarIndex(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong originalId;
        bool duplicateRejected = false;
        using (var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = recordBase.CreateOrOpenStore<ShowcaseUniqueAccount>("accounts", "Unique account examples");
            accounts.CreateIndex(account => account.AccountNumber, IndexDirection.Ascending, unique: true);
            originalId = accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 1001, Login = "ada", Name = "Ada" });
            accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 1002, Login = "grace", Name = "Grace" });
            try
            {
                accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 1001, Login = "impostor", Name = "Duplicate" });
            }
            catch (IndexContractException error) when (error.FailureKind == IndexContractFailureKind.UniqueConstraint)
            {
                duplicateRejected = true;
            }
        }
        token.ThrowIfCancellationRequested();
        bool originalSurvived;
        bool distinctSurvived;
        using (var reopened = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = reopened.OpenStore<ShowcaseUniqueAccount>("accounts");
            originalSurvived = accounts.Get(originalId)?.Login == "ada"
                && accounts.GetOrDefault(".AccountNumber", 1001)?.Login == "ada";
            distinctSurvived = accounts.GetOrDefault(".AccountNumber", 1002)?.Login == "grace";
        }
        watch.Stop();
        bool passed = duplicateRejected && originalSurvived && distinctSurvived;
        return new ScenarioResult(passed, passed ? "A duplicate account number was rejected; both original accounts survived reopen." : "Numeric uniqueness or durable account lookup did not match expectations.",
            [new("Unique key", "AccountNumber"), new("Duplicate rejected", duplicateRejected.ToString()), new("Original survived", originalSurvived.ToString()), new("Distinct key survived", distinctSurvived.ToString())],
            passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Creates a durable unique exact-string index and proves a duplicate login cannot displace the original account.<br/></summary>
    /// <param name="workspace">Owner of this isolated Fundamentals RecordBase.<br/></param>
    /// <param name="token">Cancellation signal checked before each storage phase.<br/></param>
    /// <returns>Duplicate-rejection and durable-lookup proof for an exact text key.<br/></returns>
    private static ScenarioResult UniqueStringIndex(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong originalId;
        bool duplicateRejected = false;
        using (var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = recordBase.CreateOrOpenStore<ShowcaseUniqueAccount>("accounts", "Unique account examples");
            accounts.CreateIndex(account => account.Login, IndexDirection.Ascending, unique: true);
            originalId = accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 1001, Login = "ada", Name = "Ada" });
            accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 1002, Login = "grace", Name = "Grace" });
            try
            {
                accounts.Put(new ShowcaseUniqueAccount { AccountNumber = 9999, Login = "ada", Name = "Duplicate" });
            }
            catch (IndexContractException error) when (error.FailureKind == IndexContractFailureKind.UniqueConstraint)
            {
                duplicateRejected = true;
            }
        }
        token.ThrowIfCancellationRequested();
        bool originalSurvived;
        bool distinctSurvived;
        using (var reopened = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = reopened.OpenStore<ShowcaseUniqueAccount>("accounts");
            originalSurvived = accounts.Get(originalId)?.AccountNumber == 1001
                && accounts.GetOrDefault(".Login", "ada")?.Name == "Ada";
            distinctSurvived = accounts.GetOrDefault(".Login", "grace")?.AccountNumber == 1002;
        }
        watch.Stop();
        bool passed = duplicateRejected && originalSurvived && distinctSurvived;
        return new ScenarioResult(passed, passed ? "An exact duplicate login was rejected; both original accounts survived reopen." : "String uniqueness or durable account lookup did not match expectations.",
            [new("Unique key", "Login (exact)"), new("Duplicate rejected", duplicateRejected.ToString()), new("Original survived", originalSurvived.ToString()), new("Distinct key survived", distinctSurvived.ToString())],
            passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Enforces uniqueness of a complete tenant-login key while allowing either part to repeat with a different partner.<br/></summary>
    /// <param name="workspace">Owner of this isolated Fundamentals RecordBase.<br/></param>
    /// <param name="token">Cancellation signal checked before the storage and reopen phases.<br/></param>
    /// <returns>Proof that only a duplicate complete pair fails and all accepted records survive reopen.<br/></returns>
    private static ScenarioResult UniqueCompositeIndex(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        ulong northAdaId;
        ulong southAdaId;
        ulong northGraceId;
        bool duplicateRejected = false;
        using (var recordBase = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = recordBase.CreateOrOpenStore<ShowcaseUniqueAccount>("accounts", "Unique account examples");
            var selector = new Selector<ShowcaseUniqueAccount>();
            accounts.CreateCompositeIndex("tenant-login",
                new RecordStore<ShowcaseUniqueAccount>.CompositeIndexOptions { Unique = true },
                selector.Prop(account => account.Tenant).ForIndexing(),
                selector.Prop(account => account.Login).ForIndexing());
            northAdaId = accounts.Put(new ShowcaseUniqueAccount { Tenant = "north", Login = "ada", AccountNumber = 1001, Name = "North Ada" });
            southAdaId = accounts.Put(new ShowcaseUniqueAccount { Tenant = "south", Login = "ada", AccountNumber = 1002, Name = "South Ada" });
            northGraceId = accounts.Put(new ShowcaseUniqueAccount { Tenant = "north", Login = "grace", AccountNumber = 1003, Name = "North Grace" });
            try
            {
                accounts.Put(new ShowcaseUniqueAccount { Tenant = "north", Login = "ada", AccountNumber = 9999, Name = "Duplicate" });
            }
            catch (IndexContractException error) when (error.FailureKind == IndexContractFailureKind.UniqueConstraint)
            {
                duplicateRejected = true;
            }
        }
        token.ThrowIfCancellationRequested();
        bool acceptedSurvived;
        bool onlyThreeStored;
        using (var reopened = RecordBase.CreateOrOpen(workspace.DirectoryPath, "showcase"))
        {
            var accounts = reopened.OpenStore<ShowcaseUniqueAccount>("accounts");
            acceptedSurvived = accounts.Get(northAdaId)?.Name == "North Ada"
                && accounts.Get(southAdaId)?.Name == "South Ada"
                && accounts.Get(northGraceId)?.Name == "North Grace";
            onlyThreeStored = accounts.Count == 3;
        }
        watch.Stop();
        bool passed = duplicateRejected && acceptedSurvived && onlyThreeStored;
        return new ScenarioResult(passed, passed ? "The complete tenant-login pair is unique; shared individual values and all three accepted accounts survived reopen." : "Composite uniqueness or durable account state did not match expectations.",
            [new("Named index", "tenant-login"), new("Shared login across tenants", "ada"), new("Shared tenant across logins", "north"), new("Duplicate pair rejected", duplicateRejected.ToString()), new("Accepted records after reopen", onlyThreeStored ? "3" : "unexpected")],
            passed, typeof(bool), watch.Elapsed.TotalMilliseconds);
    }
}
