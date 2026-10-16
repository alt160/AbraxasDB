using AbraxasDB;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeridianTrading;

internal sealed partial class TradingDatabase
{
    private const int SearchPageSize = 50;
    private const int MaximumSearchClauses = 12;
    private const int MaximumSearchGroups = 6;
    private const int MaximumSearchGroupDepth = 3;
    private static readonly ConditionTextOptions DeveloperConditionTextOptions = new()
    {
        Audience = ConditionTextAudience.Developer,
        Layout = ConditionTextLayout.Outline,
        Culture = CultureInfo.InvariantCulture
    };

    private static readonly SearchDatasetDefinition[] SearchDatasets =
    [
        new("orders", "Sales orders", "Orders", typeof(SalesOrder),
        [
            new("Number", "Order number", SearchValueKind.Text),
            new("Status", "Status", SearchValueKind.Text),
            new("Region", "Region", SearchValueKind.Text),
            new("OrderedAt", "Ordered at", SearchValueKind.Date),
            new("Total", "Order value", SearchValueKind.Decimal),
            new("CustomerID", "Customer identity", SearchValueKind.UInt64)
        ]),
        new("customers", "Customers", "Customers", typeof(Customer),
        [
            new("Name", "Name", SearchValueKind.Text),
            new("Code", "Customer code", SearchValueKind.Text),
            new("Email", "Email", SearchValueKind.Text),
            new("Region", "Region", SearchValueKind.Text),
            new("CreditLimit", "Credit limit", SearchValueKind.Decimal)
        ]),
        new("products", "Catalog", "Products", typeof(Product),
        [
            new("Name", "Product name", SearchValueKind.Text),
            new("SKU", "SKU", SearchValueKind.Text),
            new("Category", "Category", SearchValueKind.Text),
            new("Price", "Price", SearchValueKind.Decimal),
            new("ReorderPoint", "Reorder point", SearchValueKind.Int32)
        ]),
        new("inventory", "Inventory", "Inventory", typeof(InventoryPosition),
        [
            new("SKU", "SKU", SearchValueKind.Text),
            new("Warehouse", "Warehouse", SearchValueKind.Text),
            new("OnHand", "On hand", SearchValueKind.Int32),
            new("Reserved", "Reserved", SearchValueKind.Int32),
            new("ProductID", "Product identity", SearchValueKind.UInt64)
        ]),
        new("lines", "Order lines", "Lines", typeof(OrderLine),
        [
            new("SKU", "SKU", SearchValueKind.Text),
            new("Quantity", "Quantity", SearchValueKind.Int32),
            new("UnitPrice", "Unit price", SearchValueKind.Decimal),
            new("Total", "Line value", SearchValueKind.Decimal),
            new("ProductID", "Product identity", SearchValueKind.UInt64)
        ])
    ];

    /// <summary>
    /// Returns the allow-listed semantic surface used by Meridian's search workbench.<br/>
    /// Field type, available operators, and current source-index attachment are reported from the open stores so the browser never invents an execution capability.<br/>
    /// </summary>
    /// <returns>A browser-safe schema for dataset, field, operator, and index-state controls.<br/></returns>
    internal object SearchSchema()
        => new
        {
            datasets = SearchDatasets.Select(dataset => new
            {
                dataset.Name,
                dataset.Label,
                fields = dataset.Fields.Select(field => new
                {
                    field.Name,
                    field.Label,
                    type = field.Kind.ToString().ToLowerInvariant(),
                    indexed = HasSearchIndex(dataset.Name, field.Name),
                    compareFields = dataset.Fields.Where(candidate => candidate.Kind == field.Kind && candidate.Name != field.Name)
                        .Select(candidate => new { candidate.Name, candidate.Label }).ToArray(),
                    operators = SearchOperators(field.Kind, dataset.Fields.Any(candidate => candidate.Kind == field.Kind && candidate.Name != field.Name))
                }).ToArray(),
                resultModes = SearchResultModes(dataset).Select(mode => new
                {
                    mode.Name,
                    mode.Label,
                    mode.Description,
                    columns = mode.Columns.Select(column => new { column.Name, column.Label, type = column.Kind.ToString().ToLowerInvariant() }).ToArray()
                }).ToArray()
            }).ToArray(),
            maximumClauses = MaximumSearchClauses,
            maximumGroups = MaximumSearchGroups,
            maximumGroupDepth = MaximumSearchGroupDepth,
            pageSize = SearchPageSize,
            wordBoundary = "Whole word treats Unicode letters and digits as word characters; punctuation, whitespace, and symbols are boundaries.",
            identityModels = new
            {
                storeOwned = new
                {
                    types = new[] { nameof(Customer), nameof(SalesOrder), nameof(Product), nameof(InventoryPosition) },
                    description = "These application objects have no Abraxas identity property. Put returns the generated identity and readers expose it separately as reader.ID.",
                    code = "var customer = new Customer { Code = \"CUS-NEW\", Name = \"Identity-free model\" };\nulong customerID = Customers.Put(customer); // Customer needs no ID member"
                },
                objectMirror = new
                {
                    type = nameof(OrderLine),
                    property = nameof(OrderLine.ID),
                    configured = Lines.ObjectIDSetter is not null,
                    description = "New or replaced OrderLine objects opt in. Abraxas invokes the hook after allocating the identity and before Inheto serializes the object, so the CLR instance and stored payload receive the same value.",
                    existingRecords = "The hook is prospective: opening an existing workspace never rewrites payloads that were persisted before it was configured.",
                    code = "Lines.ObjectIDSetter = static (line, generatedID) => line.ID = generatedID;\nulong lineID = Lines.Put(line); // line.ID == lineID"
                }
            }
        };

    /// <summary>
    /// Executes an allow-listed user-authored condition through the ordinary Abraxas condition reader.<br/>
    /// Each clause is converted to a typed ConditionBuilder operation; nested all/any groups retain their Boolean precedence without evaluating caller-provided code.<br/>
    /// An exact native aggregate count completes before the bounded scalar page is read, and the two costs remain separately visible to the developer.<br/>
    /// </summary>
    /// <param name="request">Detached browser request containing one dataset and a bounded nested tree of allow-listed typed clauses.<br/></param>
    /// <returns>Rows plus exact match count, descriptor-backed condition text, executable-equivalent sample code, index eligibility, route semantics, and phase timings.<br/></returns>
    internal object SearchRecords(JsonElement request)
    {
        string datasetName = RequiredText(request, "dataset");
        SearchDatasetDefinition dataset = GetSearchDataset(datasetName);
        SearchGroupRequest root = ParseSearchRoot(request, dataset);
        SearchResultModeDefinition resultMode = ResolveSearchResultMode(dataset, OptionalText(request, "resultMode") ?? "object");

        return datasetName switch
        {
            "orders" => SearchStore(Orders, dataset, root, resultMode),
            "customers" => SearchStore(Customers, dataset, root, resultMode),
            "products" => SearchStore(Products, dataset, root, resultMode),
            "inventory" => SearchStore(Inventory, dataset, root, resultMode),
            "lines" => SearchStore(Lines, dataset, root, resultMode),
            _ => throw new ArgumentException("Unknown search dataset.")
        };
    }

    /// <summary>
    /// Builds and describes one allow-listed user-authored condition without counting matches or materializing result objects.<br/>
    /// This route lets the developer inspect the exact ConditionBuilder code and canonical descriptor before choosing to execute potentially expensive unindexed work.<br/>
    /// </summary>
    /// <param name="request">Detached browser request containing one dataset and a bounded nested tree of allow-listed typed clauses.<br/></param>
    /// <returns>The completed condition's display text, canonical descriptor, equivalent C# code, and attached-index route evidence.<br/></returns>
    internal object DescribeSearch(JsonElement request)
    {
        string datasetName = RequiredText(request, "dataset");
        SearchDatasetDefinition dataset = GetSearchDataset(datasetName);
        SearchGroupRequest root = ParseSearchRoot(request, dataset);
        SearchResultModeDefinition resultMode = ResolveSearchResultMode(dataset, OptionalText(request, "resultMode") ?? "object");

        return datasetName switch
        {
            "orders" => DescribeSearchStore(Orders, dataset, root, resultMode),
            "customers" => DescribeSearchStore(Customers, dataset, root, resultMode),
            "products" => DescribeSearchStore(Products, dataset, root, resultMode),
            "inventory" => DescribeSearchStore(Inventory, dataset, root, resultMode),
            "lines" => DescribeSearchStore(Lines, dataset, root, resultMode),
            _ => throw new ArgumentException("Unknown search dataset.")
        };
    }

    /// <summary>
    /// Retrieves one scalar object by its RecordStore identity without consulting a secondary property index.<br/>
    /// The route deliberately suppresses registered links so the workbench can isolate Fractal's direct identity lookup plus Inheto materialization from relationship hydration.<br/>
    /// </summary>
    /// <param name="entity">Allow-listed store name.<br/></param>
    /// <param name="identity">Exact persisted RecordStore identity.<br/></param>
    /// <returns>The bounded lookup result, exact developer code, and measured direct-reader duration.<br/></returns>
    internal object LookupRecord(string entity, ulong identity)
        => entity switch
        {
            "orders" => LookupStore(Orders, "Orders", identity),
            "customers" => LookupStore(Customers, "Customers", identity),
            "products" => LookupStore(Products, "Products", identity),
            "inventory" => LookupStore(Inventory, "Inventory", identity),
            "lines" => LookupStore(Lines, "Lines", identity),
            _ => throw new ArgumentException("Unknown lookup dataset.")
        };

    /// <summary>
    /// Builds the selected grid-search condition with the same allow-listed text semantics used by the dedicated workbench.<br/>
    /// This keeps the concise per-page search pill and the developer-oriented workbench semantically identical.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete domain type stored in the selected RecordStore.<br/></typeparam>
    /// <param name="store">Open store that supplies ConditionBuilder and index metadata.<br/></param>
    /// <param name="dataset">Allow-listed dataset metadata.<br/></param>
    /// <param name="fieldName">Selected text property name.<br/></param>
    /// <param name="mode">Selected text comparison mode.<br/></param>
    /// <param name="value">Literal or regex search value.<br/></param>
    /// <param name="ignoreCase">Whether comparison ignores case.<br/></param>
    /// <returns>A completed Abraxas condition and UI evidence derived from that exact condition.<br/></returns>
    private static (Condition<T>.EndCondition Condition, GridSearchEvidence Evidence) BuildGridSearch<T>(RecordStore<T> store, SearchDatasetDefinition dataset, string fieldName, string mode, string value, bool ignoreCase)
    {
        var buildTimer = Stopwatch.StartNew();
        SearchFieldDefinition field = ResolveField(dataset, fieldName);
        if (field.Kind != SearchValueKind.Text) throw new ArgumentException("Grid search fields must be text properties.");
        var clause = new SearchClauseRequest(field.Name, mode, value, "", ignoreCase);
        var root = new SearchGroupRequest("all", [new SearchNodeRequest(clause, null)]);
        Condition<T>.EndCondition condition;
        string code;
        using (ActionTiming.Calculate())
        {
            condition = BuildCondition(store, dataset, root);
            code = BuildConditionCode(dataset, root);
        }
        buildTimer.Stop();
        bool indexed = store.TryGetIndex("." + field.Name, out _);
        return (condition, new GridSearchEvidence(
            field.Name,
            field.Label,
            NormalizeOperator(field.Kind, mode),
            ignoreCase,
            indexed,
            condition.ToDisplayString(),
            RenderDeveloperCondition(condition),
            condition.ToCanonicalString(),
            code,
            SearchRouteText(indexed ? 1 : 0, 1),
            buildTimer.Elapsed.TotalMilliseconds));
    }

    /// <summary>
    /// Executes one typed condition and returns a bounded natural-order page before completing exact cardinality through the observed Abraxas plan.<br/>
    /// A direct or candidate plan retains native aggregate counting; a full Fractal scan uses a bounded two-worker identity stream whose ordering is intentionally unobservable to the count consumer.<br/>
    /// Index availability is evidence about planner inputs, not a fabricated count of records scanned or an engine CPU measurement.<br/>
    /// </summary>
    private static object SearchStore<T>(RecordStore<T> store, SearchDatasetDefinition dataset, SearchGroupRequest root, SearchResultModeDefinition resultMode)
    {
        ActionTiming.Phase("Abraxas — building typed " + dataset.Label + " condition");
        var buildTimer = Stopwatch.StartNew();
        Condition<T>.EndCondition condition;
        string display;
        string developerDisplay;
        string canonical;
        string code;
        using (ActionTiming.Calculate())
        {
            condition = BuildCondition(store, dataset, root);
            display = condition.ToDisplayString();
            developerDisplay = RenderDeveloperCondition(condition);
            canonical = condition.ToCanonicalString();
            code = BuildConditionCode(dataset, root);
        }
        double buildMilliseconds = buildTimer.Elapsed.TotalMilliseconds;
        SearchClauseRequest[] clauses = FlattenClauses(root).ToArray();

        ActionTiming.Phase("Abraxas — reading the selected bounded result shape");
        var readTimer = Stopwatch.StartNew();
        double? firstResultMilliseconds = null;
        var rows = new List<object>(SearchPageSize);
        ConditionExecutionPlanEventArgs? readerPlan = null;
        void CaptureReaderPlan(object? sender, ConditionExecutionPlanEventArgs args)
        {
            if (ReferenceEquals(args.Condition, condition)) readerPlan = args;
        }
        store.ConditionExecutionPlanned += CaptureReaderPlan;
        try
        {
            using (ActionTiming.Db())
            {
                if (resultMode.Name == "value")
                {
                    SearchFieldDefinition field = resultMode.Columns[0];
                    using var reader = store.Readers.OpenValuesWithIds(condition, "." + field.Name);
                    while (rows.Count < SearchPageSize && reader.Read())
                    {
                        rows.Add(new
                        {
                            id = reader.ID.ToString(CultureInfo.InvariantCulture),
                            value = new Dictionary<string, object?> { [field.Name] = ReadSearchScalar(reader, field.Kind) }
                        });
                        firstResultMilliseconds ??= readTimer.Elapsed.TotalMilliseconds;
                    }
                }
                else
                {
                    using var reader = store.Readers.Open(condition);
                    while (rows.Count < SearchPageSize && reader.ReadNoLinks())
                    {
                        object value = resultMode.Name == "projection" ? ProjectSearchRow(reader, dataset.Name) : reader.GetNoLinks()!;
                        rows.Add(new { id = reader.ID.ToString(CultureInfo.InvariantCulture), value });
                        firstResultMilliseconds ??= readTimer.Elapsed.TotalMilliseconds;
                    }
                }
            }
        }
        finally
        {
            store.ConditionExecutionPlanned -= CaptureReaderPlan;
        }
        readTimer.Stop();
        ConditionExecutionPlanEventArgs selectedPlan = readerPlan
            ?? throw new InvalidOperationException("Abraxas did not report the condition execution plan.");
        bool useParallelFractalCount = selectedPlan.PlanKind == ConditionExecutionPlanKind.RecordScan;

        ActionTiming.Phase(useParallelFractalCount
            ? "Abraxas — completing exact count with two physical scan workers"
            : "Abraxas — completing exact matching-record count");
        var countTimer = Stopwatch.StartNew();
        ulong matchCount = 0;
        ConditionExecutionPlanEventArgs? countPlan = null;
        void CaptureCountPlan(object? sender, ConditionExecutionPlanEventArgs args)
        {
            if (ReferenceEquals(args.Condition, condition)) countPlan = args;
        }
        store.ConditionExecutionPlanned += CaptureCountPlan;
        try
        {
            using (ActionTiming.Db())
            {
                if (useParallelFractalCount)
                {
                    foreach (ulong _ in store.FindFastIDs(condition, maxDegreeOfParallelism: 2, channelCapacity: 256, useParallel: true))
                        matchCount = checked(matchCount + 1);
                }
                else
                {
                    matchCount = store.Aggregates.Count(condition);
                }
            }
        }
        finally
        {
            store.ConditionExecutionPlanned -= CaptureCountPlan;
        }
        countTimer.Stop();
        ConditionExecutionPlanEventArgs actualCountPlan = countPlan
            ?? throw new InvalidOperationException("Abraxas did not report the exact-count execution plan.");
        if (useParallelFractalCount && actualCountPlan.PlanKind != ConditionExecutionPlanKind.RecordScan)
            throw new InvalidOperationException("The Fractal exact-count route reported a different execution plan than the bounded result reader.");

        code = BuildConditionCode(dataset, root, useParallelFractalCount);
        SearchPlanEvidence plan = BuildSearchPlanEvidence(selectedPlan, dataset, clauses);
        SearchPlanEvidence countPlanEvidence = BuildSearchPlanEvidence(actualCountPlan, dataset, clauses);
        SearchClauseEvidence[] clauseEvidence = BuildSearchClauseEvidence(store, dataset, clauses, selectedPlan);
        int completeObjects = resultMode.Name == "object" ? rows.Count : 0;
        int reducedObjects = resultMode.Name == "projection" ? rows.Count : 0;
        int scalarValues = resultMode.Name == "value" ? rows.Count : 0;

        return new
        {
            rows,
            more = matchCount > (ulong)rows.Count,
            matchCount,
            dataset = dataset.Name,
            datasetLabel = dataset.Label,
            combinator = root.Combinator,
            condition = new { display, developerDisplay, canonical, code },
            clauses = clauseEvidence,
            plan,
            countPlan = countPlanEvidence,
            route = plan.Route,
            routeKind = plan.RouteKind,
            timings = new
            {
                conditionBuildMs = buildMilliseconds,
                countMs = countTimer.Elapsed.TotalMilliseconds,
                firstResultMs = firstResultMilliseconds,
                readerMs = readTimer.Elapsed.TotalMilliseconds,
                searchCompleteMs = buildMilliseconds + countTimer.Elapsed.TotalMilliseconds + readTimer.Elapsed.TotalMilliseconds
            },
            countStrategy = useParallelFractalCount ? "parallel-fractal-scan" : "native-aggregate",
            countWorkers = useParallelFractalCount ? 2 : 1,
            countRoute = useParallelFractalCount
                ? $"FindFastIDs(condition, maxDegreeOfParallelism: 2) enumerated and discarded exact matching identities through the reported {countPlanEvidence.Label} plan after the bounded natural-order read."
                : $"Aggregates.Count(condition) used the reported {countPlanEvidence.Label} plan after the bounded natural-order read.",
            resultShape = new
            {
                resultMode.Name,
                resultMode.Label,
                resultMode.Description,
                columns = resultMode.Columns.Select(column => new { column.Name, column.Label, type = column.Kind.ToString().ToLowerInvariant() }).ToArray(),
                completeObjects,
                reducedObjects,
                scalarValues,
                code = BuildResultShapeCode(dataset, resultMode)
            },
            pageSize = SearchPageSize,
            linksSuppressed = true
        };
    }

    /// <summary>
    /// Completes one typed condition solely for developer inspection and derives route evidence from the selected store's attached source indexes.<br/>
    /// No aggregate, reader, Fractal payload access, or Inheto object materialization occurs on this preview path.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete record type for the selected allow-listed dataset.<br/></typeparam>
    /// <param name="store">Open record store supplying ConditionBuilder and attached-index metadata.<br/></param>
    /// <param name="dataset">Allow-listed dataset and field definitions.<br/></param>
    /// <param name="root">Validated bounded Boolean group tree.<br/></param>
    /// <returns>Condition descriptors, equivalent code, route classification, and measured condition-build wall time.<br/></returns>
    private static object DescribeSearchStore<T>(RecordStore<T> store, SearchDatasetDefinition dataset, SearchGroupRequest root, SearchResultModeDefinition resultMode)
    {
        ActionTiming.Phase("Abraxas — describing typed " + dataset.Label + " condition");
        var buildTimer = Stopwatch.StartNew();
        Condition<T>.EndCondition condition;
        string code;
        using (ActionTiming.Calculate())
        {
            condition = BuildCondition(store, dataset, root);
            code = BuildConditionCode(dataset, root);
        }
        buildTimer.Stop();

        SearchClauseRequest[] clauses = FlattenClauses(root).ToArray();
        int indexedClauses = clauses.Count(clause => store.TryGetIndex("." + ResolveField(dataset, clause.Field).Name, out _));
        return new
        {
            dataset = dataset.Name,
            datasetLabel = dataset.Label,
            condition = new
            {
                display = condition.ToDisplayString(),
                developerDisplay = RenderDeveloperCondition(condition),
                canonical = condition.ToCanonicalString(),
                code
            },
            route = SearchRouteText(indexedClauses, clauses.Length),
            routeKind = indexedClauses == clauses.Length ? "indexed" : indexedClauses == 0 ? "scan" : "hybrid",
            resultShape = new
            {
                resultMode.Name,
                resultMode.Label,
                resultMode.Description,
                columns = resultMode.Columns.Select(column => new { column.Name, column.Label, type = column.Kind.ToString().ToLowerInvariant() }).ToArray(),
                code = BuildResultShapeCode(dataset, resultMode)
            },
            conditionBuildMs = buildTimer.Elapsed.TotalMilliseconds,
            execution = "Condition description only — no count, reader, or object materialization was performed."
        };
    }

    /// <summary>Reads one identity through the public bounded identity-reader route and suppresses relationship expansion.<br/></summary>
    private static object LookupStore<T>(RecordStore<T> store, string storeName, ulong identity)
    {
        ActionTiming.Phase("Abraxas — direct Fractal identity lookup and Inheto materialization");
        var timer = Stopwatch.StartNew();
        T? value = default;
        bool found;
        using (ActionTiming.Db())
        using (var reader = store.Readers.Open(new[] { identity }))
        {
            found = reader.ReadNoLinks();
            if (found) value = reader.GetNoLinks();
        }
        timer.Stop();
        return new
        {
            found,
            id = identity.ToString(CultureInfo.InvariantCulture),
            value,
            elapsedMs = timer.Elapsed.TotalMilliseconds,
            route = "Record identity → Fractal direct lookup → Inheto scalar object materialization",
            code = $"using var reader = {storeName}.Readers.Open(new[] {{ recordID }});\nif (reader.ReadNoLinks())\n    var value = reader.GetNoLinks();",
            linksSuppressed = true
        };
    }

    /// <summary>
    /// Builds one root ConditionBuilder tree from a validated nested all/any request.<br/>
    /// Each child group becomes one completed grouped operand, preserving the exact precedence selected in the browser.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete record type for the selected allow-listed dataset.<br/></typeparam>
    /// <param name="store">Open record store that supplies root and group clauses.<br/></param>
    /// <param name="dataset">Allow-listed field and scalar-type metadata.<br/></param>
    /// <param name="root">Validated root group whose children contain at least one direct clause.<br/></param>
    /// <returns>A completed condition suitable for readers, aggregates, descriptors, and index planning.<br/></returns>
    private static Condition<T>.EndCondition BuildCondition<T>(RecordStore<T> store, SearchDatasetDefinition dataset, SearchGroupRequest root)
        => BuildConditionGroup(store, dataset, root, isRoot: true);

    /// <summary>
    /// Recursively composes one homogeneous Boolean group through the public ConditionBuilder grouping surface.<br/>
    /// A direct clause is selected as the first operand because ConditionBuilder starts groups from a typed clause.<br/>
    /// Direct siblings continue fluently through <c>AND</c>/<c>OR</c>; only actual child groups use completed-condition containment so their internal precedence remains intact without adding ceremony to ordinary clauses.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete record type for the selected allow-listed dataset.<br/></typeparam>
    /// <param name="store">Open store supplying <c>Where</c> for the root and <c>GroupCond</c> for nested operands.<br/></param>
    /// <param name="dataset">Allow-listed field metadata used to choose the exact typed operator family.<br/></param>
    /// <param name="group">Validated group to compose.<br/></param>
    /// <param name="isRoot">Whether the group begins at the store's root <c>Where</c> clause.<br/></param>
    /// <returns>A completed condition representing the entire group.<br/></returns>
    private static Condition<T>.EndCondition BuildConditionGroup<T>(RecordStore<T> store, SearchDatasetDefinition dataset, SearchGroupRequest group, bool isRoot)
    {
        SearchNodeRequest first = group.Children.First(node => node.Clause is not null);
        Condition<T>.Clause start = isRoot ? store.Where : store.GroupCond;
        Condition<T>.ContinueOrEndCondition continuation = BuildClause(start, ResolveField(dataset, first.Clause!.Field), first.Clause);
        foreach (SearchNodeRequest node in group.Children)
        {
            if (ReferenceEquals(node, first)) continue;
            if (node.Clause is { } clause)
            {
                Condition<T>.Clause next = group.Combinator == "all" ? continuation.AND : continuation.OR;
                continuation = BuildClause(next, ResolveField(dataset, clause.Field), clause);
                continue;
            }

            Condition<T>.EndCondition right = BuildConditionGroup(store, dataset, node.Group!, isRoot: false);
            continuation = group.Combinator == "all" ? continuation.And(right) : continuation.Or(right);
        }
        return continuation.EndCondition;
    }

    /// <summary>Translates one allow-listed typed clause into the corresponding ConditionBuilder continuation.<br/></summary>
    private static Condition<T>.ContinueOrEndCondition BuildClause<T>(Condition<T>.Clause start, SearchFieldDefinition field, SearchClauseRequest clause)
    {
        string op = NormalizeOperator(field.Kind, clause.Operator);
        string path = "." + field.Name;
        return field.Kind switch
        {
            SearchValueKind.Text => BuildTextClause(start, path, op, clause.Value, clause.IgnoreCase),
            SearchValueKind.Int32 => BuildInt32Clause(start, path, op, clause.Value, clause.Value2),
            SearchValueKind.UInt64 => BuildUInt64Clause(start, path, op, clause.Value, clause.Value2),
            SearchValueKind.Decimal => BuildDecimalClause(start, path, op, clause.Value, clause.Value2),
            SearchValueKind.Date => BuildDateClause(start, path, op, clause.Value, clause.Value2),
            _ => throw new ArgumentException("Unsupported search field type.")
        };
    }

    /// <summary>Builds literal, Unicode whole-word, or caller-authored regex string semantics.<br/></summary>
    private static Condition<T>.ContinueOrEndCondition BuildTextClause<T>(Condition<T>.Clause start, string path, string op, string value, bool ignoreCase)
    {
        if (value.Length == 0) throw new ArgumentException("Text conditions require a value.");
        var text = start.PropPath(path).AsString;
        return op switch
        {
            "contains" => text.Contains(value, ignoreCase),
            "starts" => text.StartsWith(value, ignoreCase),
            "ends" => text.EndsWith(value, ignoreCase),
            "exact" => text.EqualTo(value, ignoreCase),
            "whole" => text.MatchesPattern(WholeWordPattern(value), ignoreCase),
            "regex" => text.MatchesPattern(ValidateRegex(value, ignoreCase), ignoreCase),
            _ => throw new ArgumentException("Unsupported text comparison.")
        };
    }

    /// <summary>Builds one checked Int32 comparison from invariant user input.<br/></summary>
    private static Condition<T>.ContinueOrEndCondition BuildInt32Clause<T>(Condition<T>.Clause start, string path, string op, string value, string value2)
    {
        var number = start.PropPath(path).AsInt32;
        if (IsPropertyComparison(op))
        {
            string rightPath = "." + value;
            return op switch
            {
                "propEqual" => number.PropEqualTo(rightPath).AsInt32,
                "propNotEqual" => number.PropNotEqualTo(rightPath).AsInt32,
                "propGreater" => number.PropGreaterThan(rightPath).AsInt32,
                "propGreaterOrEqual" => number.PropGreaterOrEqual(rightPath).AsInt32,
                "propLess" => number.PropLessThan(rightPath).AsInt32,
                "propLessOrEqual" => number.PropLessOrEqual(rightPath).AsInt32,
                _ => throw new ArgumentException("Unsupported integer property comparison.")
            };
        }
        int first = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return op switch
        {
            "equal" => number.EqualTo(first),
            "greater" => number.GreaterThan(first),
            "greaterOrEqual" => number.GreaterOrEqual(first),
            "less" => number.LessThan(first),
            "lessOrEqual" => number.LessOrEqual(first),
            "between" => number.Between(first, int.Parse(value2, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException("Unsupported integer comparison.")
        };
    }

    /// <summary>Builds one checked UInt64 comparison from invariant user input.<br/></summary>
    private static Condition<T>.ContinueOrEndCondition BuildUInt64Clause<T>(Condition<T>.Clause start, string path, string op, string value, string value2)
    {
        var number = start.PropPath(path).AsUInt64;
        if (IsPropertyComparison(op))
        {
            string rightPath = "." + value;
            return op switch
            {
                "propEqual" => number.PropEqualTo(rightPath).AsUInt64,
                "propNotEqual" => number.PropNotEqualTo(rightPath).AsUInt64,
                "propGreater" => number.PropGreaterThan(rightPath).AsUInt64,
                "propGreaterOrEqual" => number.PropGreaterOrEqual(rightPath).AsUInt64,
                "propLess" => number.PropLessThan(rightPath).AsUInt64,
                "propLessOrEqual" => number.PropLessOrEqual(rightPath).AsUInt64,
                _ => throw new ArgumentException("Unsupported identity property comparison.")
            };
        }
        ulong first = ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return op switch
        {
            "equal" => number.EqualTo(first),
            "greater" => number.GreaterThan(first),
            "greaterOrEqual" => number.GreaterOrEqual(first),
            "less" => number.LessThan(first),
            "lessOrEqual" => number.LessOrEqual(first),
            "between" => number.Between(first, ulong.Parse(value2, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException("Unsupported identity comparison.")
        };
    }

    /// <summary>Builds one checked Decimal comparison from invariant user input.<br/></summary>
    private static Condition<T>.ContinueOrEndCondition BuildDecimalClause<T>(Condition<T>.Clause start, string path, string op, string value, string value2)
    {
        var number = start.PropPath(path).AsDecimal;
        if (IsPropertyComparison(op))
        {
            string rightPath = "." + value;
            return op switch
            {
                "propEqual" => number.PropEqualTo(rightPath).AsDecimal,
                "propNotEqual" => number.PropNotEqualTo(rightPath).AsDecimal,
                "propGreater" => number.PropGreaterThan(rightPath).AsDecimal,
                "propGreaterOrEqual" => number.PropGreaterOrEqual(rightPath).AsDecimal,
                "propLess" => number.PropLessThan(rightPath).AsDecimal,
                "propLessOrEqual" => number.PropLessOrEqual(rightPath).AsDecimal,
                _ => throw new ArgumentException("Unsupported decimal property comparison.")
            };
        }
        decimal first = decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        return op switch
        {
            "equal" => number.EqualTo(first),
            "greater" => number.GreaterThan(first),
            "greaterOrEqual" => number.GreaterOrEqual(first),
            "less" => number.LessThan(first),
            "lessOrEqual" => number.LessOrEqual(first),
            "between" => number.Between(first, decimal.Parse(value2, NumberStyles.Number, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException("Unsupported decimal comparison.")
        };
    }

    /// <summary>
    /// Builds date semantics using half-open whole-day ranges so a date chosen in the browser does not accidentally mean only midnight.<br/>
    /// Between includes both selected calendar dates by using the instant immediately after the second day as its exclusive upper bound.<br/>
    /// </summary>
    private static Condition<T>.ContinueOrEndCondition BuildDateClause<T>(Condition<T>.Clause start, string path, string op, string value, string value2)
    {
        DateTime first = ParseDate(value);
        DateTime next = first.AddDays(1);
        return op switch
        {
            "on" => start.PropPath(path).AsDateTime.GreaterOrEqual(first).AND.PropPath(path).AsDateTime.LessThan(next),
            "before" => start.PropPath(path).AsDateTime.LessThan(first),
            "onOrBefore" => start.PropPath(path).AsDateTime.LessThan(next),
            "after" => start.PropPath(path).AsDateTime.GreaterOrEqual(next),
            "onOrAfter" => start.PropPath(path).AsDateTime.GreaterOrEqual(first),
            "between" => start.PropPath(path).AsDateTime.GreaterOrEqual(first).AND.PropPath(path).AsDateTime.LessThan(ParseDate(value2).AddDays(1)),
            _ => throw new ArgumentException("Unsupported date comparison.")
        };
    }

    /// <summary>Returns the allow-listed grid field names without duplicating backend semantics in the browser bridge.<br/></summary>
    private static string DefaultSearchField(string entity)
        => entity switch
        {
            "orders" => "Number",
            "customers" => "Name",
            "products" => "Name",
            "inventory" => "SKU",
            "lines" => "SKU",
            _ => throw new ArgumentException("Unknown dataset.")
        };

    /// <summary>Reports whether the selected open store currently owns an attached source-property index.<br/></summary>
    private bool HasSearchIndex(string entity, string field)
        => entity switch
        {
            "orders" => Orders.TryGetIndex("." + field, out _),
            "customers" => Customers.TryGetIndex("." + field, out _),
            "products" => Products.TryGetIndex("." + field, out _),
            "inventory" => Inventory.TryGetIndex("." + field, out _),
            "lines" => Lines.TryGetIndex("." + field, out _),
            _ => false
        };

    /// <summary>Returns one validated dataset definition by its browser-safe name.<br/></summary>
    private static SearchDatasetDefinition GetSearchDataset(string name)
        => SearchDatasets.FirstOrDefault(item => item.Name == name) ?? throw new ArgumentException("Unknown search dataset.");

    /// <summary>Returns one validated field definition and rejects arbitrary property paths from the browser.<br/></summary>
    private static SearchFieldDefinition ResolveField(SearchDatasetDefinition dataset, string field)
        => dataset.Fields.FirstOrDefault(item => item.Name == field) ?? throw new ArgumentException($"'{field}' is not a searchable {dataset.Label} field.");

    /// <summary>Parses one detached clause without accepting executable expressions or arbitrary paths.<br/></summary>
    private static SearchClauseRequest ParseSearchClause(JsonElement element)
        => new(
            RequiredText(element, "field"),
            RequiredText(element, "operator"),
            RequiredText(element, "value", allowEmpty: true),
            element.TryGetProperty("value2", out JsonElement second) ? second.GetString() ?? "" : "",
            !element.TryGetProperty("ignoreCase", out JsonElement ignoreCase) || ignoreCase.GetBoolean());

    /// <summary>
    /// Parses either the current nested root-group contract or the earlier flat all/any contract used by retained verification artifacts.<br/>
    /// Every property path, scalar family, operator, group count, clause count, and nesting level is validated before a condition builder is created.<br/>
    /// </summary>
    /// <param name="request">Detached browser request containing a dataset and nested root group or legacy flat clause array.<br/></param>
    /// <param name="dataset">Selected allow-listed dataset definition.<br/></param>
    /// <returns>A bounded validated root group with at least one direct clause in every group.<br/></returns>
    private static SearchGroupRequest ParseSearchRoot(JsonElement request, SearchDatasetDefinition dataset)
    {
        int clauseCount = 0;
        int groupCount = 0;
        SearchGroupRequest root;
        if (request.TryGetProperty("root", out JsonElement rootElement))
        {
            root = ParseSearchGroup(rootElement, dataset, depth: 1, ref clauseCount, ref groupCount);
        }
        else
        {
            string combinator = NormalizeCombinator(RequiredText(request, "combinator"));
            if (!request.TryGetProperty("clauses", out JsonElement clauses) || clauses.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Supply one or more search conditions.");
            var children = clauses.EnumerateArray()
                .Select(element => new SearchNodeRequest(ParseSearchClause(element), null))
                .ToArray();
            clauseCount = children.Length;
            groupCount = 1;
            root = new SearchGroupRequest(combinator, children);
        }

        if (clauseCount is < 1 or > MaximumSearchClauses)
            throw new ArgumentException($"Supply between one and {MaximumSearchClauses} search conditions.");
        if (groupCount > MaximumSearchGroups)
            throw new ArgumentException($"Supply no more than {MaximumSearchGroups} condition groups.");
        foreach (SearchClauseRequest clause in FlattenClauses(root))
        {
            SearchFieldDefinition field = ResolveField(dataset, clause.Field);
            string operation = NormalizeOperator(field.Kind, clause.Operator);
            if (!IsPropertyComparison(operation)) continue;
            SearchFieldDefinition right = ResolveField(dataset, clause.Value);
            if (field.Name == right.Name || field.Kind != right.Kind)
                throw new ArgumentException("Property comparisons require a different field with the same scalar type.");
        }
        return root;
    }

    /// <summary>
    /// Parses one recursively nested Boolean group while enforcing Meridian's bounded browser-authored condition surface.<br/>
    /// Each group must contain a direct clause so ConditionBuilder can establish its typed first operand before completed subgroups are attached.<br/>
    /// </summary>
    /// <param name="element">Detached JSON group with a combinator and children.<br/></param>
    /// <param name="dataset">Allow-listed dataset used to reject arbitrary field paths immediately.<br/></param>
    /// <param name="depth">One-based nesting depth including the root group.<br/></param>
    /// <param name="clauseCount">Running total used to enforce the clause limit across the complete tree.<br/></param>
    /// <param name="groupCount">Running total used to enforce the group limit across the complete tree.<br/></param>
    /// <returns>A validated immutable group request.<br/></returns>
    private static SearchGroupRequest ParseSearchGroup(JsonElement element, SearchDatasetDefinition dataset, int depth, ref int clauseCount, ref int groupCount)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("A condition group must be an object.");
        if (depth > MaximumSearchGroupDepth) throw new ArgumentException($"Condition groups may be nested at most {MaximumSearchGroupDepth} levels.");
        if (++groupCount > MaximumSearchGroups) throw new ArgumentException($"Supply no more than {MaximumSearchGroups} condition groups.");
        string combinator = NormalizeCombinator(RequiredText(element, "combinator"));
        if (!element.TryGetProperty("children", out JsonElement childrenElement) || childrenElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Each condition group requires children.");

        var children = new List<SearchNodeRequest>();
        foreach (JsonElement child in childrenElement.EnumerateArray())
        {
            string kind = RequiredText(child, "kind").ToLowerInvariant();
            if (kind == "clause")
            {
                SearchClauseRequest clause = ParseSearchClause(child);
                _ = ResolveField(dataset, clause.Field);
                if (++clauseCount > MaximumSearchClauses) throw new ArgumentException($"Supply no more than {MaximumSearchClauses} search conditions.");
                children.Add(new SearchNodeRequest(clause, null));
            }
            else if (kind == "group")
            {
                children.Add(new SearchNodeRequest(null, ParseSearchGroup(child, dataset, depth + 1, ref clauseCount, ref groupCount)));
            }
            else
            {
                throw new ArgumentException("Condition children must be clauses or groups.");
            }
        }

        if (children.Count == 0) throw new ArgumentException("Each condition group requires at least one condition.");
        if (!children.Any(child => child.Clause is not null))
            throw new ArgumentException("Each condition group requires at least one direct condition before nested groups can be combined.");
        return new SearchGroupRequest(combinator, children);
    }

    /// <summary>
    /// Validates and canonicalizes the two browser-visible Boolean group policies.<br/>
    /// Keeping this boundary host-owned prevents an arbitrary browser token from selecting or constructing a condition operation.<br/>
    /// </summary>
    /// <param name="value">Detached all/any token supplied for one condition group.<br/></param>
    /// <returns>The lowercase canonical combinator consumed by the recursive builder.<br/></returns>
    private static string NormalizeCombinator(string value)
        => value.ToLowerInvariant() switch
        {
            "all" => "all",
            "any" => "any",
            _ => throw new ArgumentException("Choose whether all or any conditions in each group must match.")
        };

    /// <summary>
    /// Enumerates direct clauses in stable depth-first display order for route and index evidence.<br/>
    /// The traversal does not flatten execution semantics; it is only a reporting projection over the already preserved group tree.<br/>
    /// </summary>
    /// <param name="group">Validated root or nested group to inspect.<br/></param>
    /// <returns>Every direct clause exactly once in user-visible tree order.<br/></returns>
    private static IEnumerable<SearchClauseRequest> FlattenClauses(SearchGroupRequest group)
    {
        foreach (SearchNodeRequest node in group.Children)
        {
            if (node.Clause is { } clause) yield return clause;
            else foreach (SearchClauseRequest nested in FlattenClauses(node.Group!)) yield return nested;
        }
    }

    /// <summary>Reads one required browser string and provides a concise validation failure.<br/></summary>
    private static string RequiredText(JsonElement element, string property, bool allowEmpty = false)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Missing {property}.");
        string text = value.GetString() ?? "";
        if (!allowEmpty && string.IsNullOrWhiteSpace(text)) throw new ArgumentException($"Missing {property}.");
        return allowEmpty ? text : text.Trim();
    }

    /// <summary>Validates and canonicalizes an operator for the selected typed field family.<br/></summary>
    private static string NormalizeOperator(SearchValueKind kind, string value)
    {
        string[] allowed = kind switch
        {
            SearchValueKind.Text => ["contains", "starts", "ends", "exact", "whole", "regex"],
            SearchValueKind.Date => ["on", "before", "onOrBefore", "after", "onOrAfter", "between"],
            _ => ["equal", "greater", "greaterOrEqual", "less", "lessOrEqual", "between", "propEqual", "propNotEqual", "propGreater", "propGreaterOrEqual", "propLess", "propLessOrEqual"]
        };
        string? match = allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException($"'{value}' is not valid for a {kind} field.");
    }

    /// <summary>Returns operator metadata in the same order used by the workbench controls.<br/></summary>
    private static object[] SearchOperators(SearchValueKind kind, bool hasComparableField)
        => kind switch
        {
            SearchValueKind.Text =>
            [
                new { value = "contains", label = "Contains" }, new { value = "starts", label = "Starts with" },
                new { value = "ends", label = "Ends with" }, new { value = "exact", label = "Exact value" },
                new { value = "whole", label = "Whole word" }, new { value = "regex", label = "Regular expression" }
            ],
            SearchValueKind.Date =>
            [
                new { value = "on", label = "On" }, new { value = "before", label = "Before" },
                new { value = "onOrBefore", label = "On or before" }, new { value = "after", label = "After" },
                new { value = "onOrAfter", label = "On or after" }, new { value = "between", label = "Between dates" }
            ],
            _ => NumericSearchOperators(hasComparableField)
        };

    /// <summary>Creates an explicit Unicode letter-or-digit boundary expression for whole-word matching.<br/></summary>
    private static string WholeWordPattern(string value)
        => @"(?<![\p{L}\p{N}])" + Regex.Escape(value) + @"(?![\p{L}\p{N}])";

    /// <summary>Rejects malformed or excessively large regular expressions before Abraxas prepares the condition.<br/></summary>
    private static string ValidateRegex(string pattern, bool ignoreCase)
    {
        if (pattern.Length is 0 or > 256) throw new ArgumentException("Regular expressions must contain 1–256 characters.");
        _ = new Regex(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromMilliseconds(250));
        return pattern;
    }

    /// <summary>Parses an ISO browser date without timezone conversion.<br/></summary>
    private static DateTime ParseDate(string value)
        => DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Explains the planner inputs without misreporting returned rows as records scanned.<br/></summary>
    private static string SearchRouteText(int indexedClauses, int totalClauses)
        => indexedClauses == totalClauses
            ? "Attached LibraDex source index for every clause → candidate identities → matching Fractal payloads → Inheto scalar objects"
            : indexedClauses == 0
                ? "No attached source index for these clauses → Fractal record traversal → Inheto property-path evaluation and scalar objects"
                : $"{indexedClauses} of {totalClauses} clauses have attached LibraDex indexes → indexed candidates plus Inheto residual evaluation → matching scalar objects";

    /// <summary>
    /// Produces executable-equivalent ConditionBuilder sample code for the exact nested user semantics and selected count route.<br/>
    /// Direct siblings use fluent <c>AND</c>/<c>OR</c> continuations, while completed subgroup operands use <c>And</c>/<c>Or</c> containment to preserve precedence.<br/>
    /// The bounded result reader appears before the count so the code mirrors Search and lookup's natural-order-first execution contract.<br/>
    /// </summary>
    /// <param name="dataset">Allow-listed store variable and field metadata.<br/></param>
    /// <param name="root">Validated nested root group.<br/></param>
    /// <param name="useParallelFractalCount">Whether exact cardinality is produced by the bounded two-worker physical scan route rather than the native aggregate.<br/></param>
    /// <returns>Concise developer-facing C# that preserves the selected Boolean precedence.<br/></returns>
    private static string BuildConditionCode(SearchDatasetDefinition dataset, SearchGroupRequest root, bool useParallelFractalCount = false)
    {
        var code = new StringBuilder("var condition = ");
        AppendConditionGroupCode(code, dataset, root, isRoot: true, indent: 0);
        code.Append(";\n\nusing var reader = ").Append(dataset.StoreVariable).Append(".Readers.Open(condition);");
        if (useParallelFractalCount)
        {
            code.Append("\n\nulong matches = 0;")
                .Append("\nforeach (ulong _ in ").Append(dataset.StoreVariable).Append(".FindFastIDs(condition, maxDegreeOfParallelism: 2, channelCapacity: 256, useParallel: true))")
                .Append("\n    matches = checked(matches + 1);");
        }
        else
        {
            code.Append("\n\nulong matches = ").Append(dataset.StoreVariable).Append(".Aggregates.Count(condition);");
        }
        return code.ToString();
    }

    /// <summary>
    /// Appends one fluent root or nested group expression while keeping only completed subgroup operands visibly parenthesized.<br/>
    /// The first direct clause may be moved before sibling subgroups because every group uses one commutative Boolean operator; the logical condition remains unchanged.<br/>
    /// </summary>
    /// <param name="code">Destination for developer-facing source text.<br/></param>
    /// <param name="dataset">Allow-listed store and field metadata.<br/></param>
    /// <param name="group">Validated homogeneous all/any group.<br/></param>
    /// <param name="isRoot">Whether to begin from <c>Where</c> instead of <c>GroupCond</c>.<br/></param>
    /// <param name="indent">Current visual indentation level for nested expressions.<br/></param>
    private static void AppendConditionGroupCode(StringBuilder code, SearchDatasetDefinition dataset, SearchGroupRequest group, bool isRoot, int indent)
    {
        SearchNodeRequest first = group.Children.First(node => node.Clause is not null);
        code.Append(dataset.StoreVariable).Append(isRoot ? ".Where" : ".GroupCond");
        AppendClauseCode(code, dataset, first.Clause!);
        foreach (SearchNodeRequest node in group.Children)
        {
            if (ReferenceEquals(node, first)) continue;
            if (node.Clause is { } clause)
            {
                code.Append('\n').Append(' ', indent + 4).Append(group.Combinator == "all" ? ".AND" : ".OR");
                AppendClauseCode(code, dataset, clause);
            }
            else
            {
                code.Append('\n').Append(' ', indent + 4).Append(group.Combinator == "all" ? ".And(" : ".Or(");
                AppendConditionGroupCode(code, dataset, node.Group!, isRoot: false, indent + 4);
                code.Append(')');
            }
        }
        code.Append("\n").Append(' ', indent + 4).Append(".EndCondition");
    }

    /// <summary>
    /// Appends one allow-listed typed property selector and its validated scalar operation.<br/>
    /// Values are rendered as literals for explanation only; runtime execution uses the already built condition and never compiles this text.<br/>
    /// </summary>
    /// <param name="code">Destination for the developer-facing sample expression.<br/></param>
    /// <param name="dataset">Allow-listed field metadata used to select the property and scalar family.<br/></param>
    /// <param name="clause">Validated typed clause whose exact operator and value are rendered.<br/></param>
    private static void AppendClauseCode(StringBuilder code, SearchDatasetDefinition dataset, SearchClauseRequest clause)
    {
        SearchFieldDefinition field = ResolveField(dataset, clause.Field);
        code.Append(".PropPath(\".").Append(field.Name).Append("\").").Append(SearchTypeCode(field.Kind)).Append('.');
        code.Append(SearchOperationCode(dataset, field, clause));
    }

    /// <summary>Maps one field family to its exact untyped-property ConditionBuilder selector.<br/></summary>
    private static string SearchTypeCode(SearchValueKind kind)
        => kind switch
        {
            SearchValueKind.Text => "AsString",
            SearchValueKind.Int32 => "AsInt32",
            SearchValueKind.UInt64 => "AsUInt64",
            SearchValueKind.Decimal => "AsDecimal",
            SearchValueKind.Date => "AsDateTime",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    /// <summary>Formats one already validated clause as concise C# without evaluating it.<br/></summary>
    private static string SearchOperationCode(SearchDatasetDefinition dataset, SearchFieldDefinition field, SearchClauseRequest clause)
    {
        string op = NormalizeOperator(field.Kind, clause.Operator);
        if (IsPropertyComparison(op))
        {
            SearchFieldDefinition right = ResolveField(dataset, clause.Value);
            string method = op switch
            {
                "propEqual" => "PropEqualTo",
                "propNotEqual" => "PropNotEqualTo",
                "propGreater" => "PropGreaterThan",
                "propGreaterOrEqual" => "PropGreaterOrEqual",
                "propLess" => "PropLessThan",
                "propLessOrEqual" => "PropLessOrEqual",
                _ => throw new ArgumentException("Unsupported property comparison.")
            };
            return $"{method}(\".{right.Name}\").{SearchTypeCode(right.Kind)}";
        }
        string first = field.Kind switch
        {
            SearchValueKind.Text => CSharpString(op == "whole" ? WholeWordPattern(clause.Value) : clause.Value),
            SearchValueKind.Date => $"DateTime.Parse({CSharpString(clause.Value)}, CultureInfo.InvariantCulture)",
            SearchValueKind.Decimal => clause.Value + "m",
            SearchValueKind.UInt64 => clause.Value + "UL",
            _ => clause.Value
        };
        string second = field.Kind switch
        {
            SearchValueKind.Date => $"DateTime.Parse({CSharpString(clause.Value2)}, CultureInfo.InvariantCulture)",
            SearchValueKind.Decimal => clause.Value2 + "m",
            SearchValueKind.UInt64 => clause.Value2 + "UL",
            _ => clause.Value2
        };
        if (field.Kind == SearchValueKind.Text)
        {
            string method = op switch { "contains" => "Contains", "starts" => "StartsWith", "ends" => "EndsWith", "exact" => "EqualTo", _ => "MatchesPattern" };
            return $"{method}({first}, ignoreCase: {clause.IgnoreCase.ToString().ToLowerInvariant()})";
        }
        if (field.Kind == SearchValueKind.Date)
        {
            string repeatedPath = $".AND.PropPath(\".{field.Name}\").AsDateTime";
            return op switch
            {
                "on" => $"GreaterOrEqual({first})\n    {repeatedPath}.LessThan({first}.AddDays(1))",
                "before" => $"LessThan({first})",
                "onOrBefore" => $"LessThan({first}.AddDays(1))",
                "after" => $"GreaterOrEqual({first}.AddDays(1))",
                "onOrAfter" => $"GreaterOrEqual({first})",
                "between" => $"GreaterOrEqual({first})\n    {repeatedPath}.LessThan({second}.AddDays(1))",
                _ => throw new ArgumentException("Unsupported date comparison.")
            };
        }
        string numericMethod = op switch
        {
            "equal" => "EqualTo",
            "greater" => "GreaterThan",
            "greaterOrEqual" => "GreaterOrEqual",
            "less" => "LessThan",
            "lessOrEqual" => "LessOrEqual",
            "between" => "Between",
            _ => throw new ArgumentException("Unsupported comparison.")
        };
        return op == "between" ? $"{numericMethod}({first}, {second})" : $"{numericMethod}({first})";
    }

    /// <summary>Produces a C#-compatible quoted string for developer-facing sample code.<br/></summary>
    private static string CSharpString(string value) => JsonSerializer.Serialize(value);

    /// <summary>Defines one safe browser-selectable store and its searchable fields.<br/></summary>
    private sealed record SearchDatasetDefinition(string Name, string Label, string StoreVariable, Type RecordType, SearchFieldDefinition[] Fields);

    /// <summary>Defines one safe property path and the CLR comparison family Abraxas must use.<br/></summary>
    private sealed record SearchFieldDefinition(string Name, string Label, SearchValueKind Kind);

    /// <summary>Contains one detached browser clause after property names have been separated from executable code.<br/></summary>
    private sealed record SearchClauseRequest(string Field, string Operator, string Value, string Value2, bool IgnoreCase);

    /// <summary>Contains one validated Boolean group whose children are direct clauses or nested groups.<br/></summary>
    private sealed record SearchGroupRequest(string Combinator, IReadOnlyList<SearchNodeRequest> Children);

    /// <summary>Represents one unambiguous condition-tree child; exactly one of clause or group is populated.<br/></summary>
    private sealed record SearchNodeRequest(SearchClauseRequest? Clause, SearchGroupRequest? Group);

    /// <summary>
    /// Carries one grid condition's developer evidence plus exact count and bounded-reader metrics.<br/>
    /// Mutable measurements are populated only by the immediately following page read and then serialized as one immutable response snapshot.<br/>
    /// </summary>
    private sealed class GridSearchEvidence
    {
        /// <summary>Creates evidence from the already built and rendered single-clause grid condition.<br/></summary>
        public GridSearchEvidence(string field, string fieldLabel, string mode, bool ignoreCase, bool indexed, string display, string developerDisplay, string canonical, string code, string route, double conditionBuildMs)
        {
            Field = field;
            FieldLabel = fieldLabel;
            Mode = mode;
            IgnoreCase = ignoreCase;
            Indexed = indexed;
            Display = display;
            DeveloperDisplay = developerDisplay;
            Canonical = canonical;
            Code = code;
            Route = route;
            ConditionBuildMs = conditionBuildMs;
        }

        public string Field { get; }
        public string FieldLabel { get; }
        public string Mode { get; }
        public bool IgnoreCase { get; }
        public bool Indexed { get; }
        public string Display { get; }
        public string DeveloperDisplay { get; }
        public string Canonical { get; }
        public string Code { get; }
        public string Route { get; }
        public double ConditionBuildMs { get; }
        public ulong MatchCount { get; set; }
        public double CountMs { get; set; }
        public double? FirstResultMs { get; set; }
        public double ReaderMs { get; set; }
        public double SearchCompleteMs => ConditionBuildMs + CountMs + ReaderMs;
        public string CountRoute => "Aggregates.Count(condition) exact matching-record count";
    }

    /// <summary>Identifies the typed ConditionBuilder family used for one allow-listed field.<br/></summary>
    private enum SearchValueKind { Text, Int32, UInt64, Decimal, Date }

    /// <summary>
    /// Renders one completed condition through Abraxas's descriptor-backed developer presentation rather than reconstructing semantics in Meridian.<br/>
    /// The invariant culture makes copied evidence stable across machines, while the outline layout exposes property paths, CLR types, Boolean grouping, and comparison options.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete domain type owned by the completed condition.<br/></typeparam>
    /// <param name="condition">Completed Abraxas condition whose native semantic descriptor should be rendered.<br/></param>
    /// <returns>Developer-oriented condition text generated entirely by <c>Condition.ToDisplayString</c>.<br/></returns>
    private static string RenderDeveloperCondition<T>(Condition<T>.EndCondition condition)
        => condition.ToDisplayString(DeveloperConditionTextOptions);

    /// <summary>
    /// Returns one optional trimmed browser string without treating a missing property as an error.<br/>
    /// The helper is used only for bounded enum-like UI state whose absence has a documented server-owned default.<br/>
    /// </summary>
    /// <param name="element">Detached browser request object.<br/></param>
    /// <param name="property">Expected optional string property.<br/></param>
    /// <returns>The trimmed nonempty value, or null when the property is absent or blank.<br/></returns>
    private static string? OptionalText(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>
    /// Builds numeric literal operators and, only when the dataset owns a compatible peer field, the property-to-property comparison operators.<br/>
    /// Keeping unavailable comparisons out of the browser prevents invalid right-property choices without weakening Abraxas's broader numeric promotion surface.<br/>
    /// </summary>
    /// <param name="hasComparableField">Whether another allow-listed field has the same declared scalar family.<br/></param>
    /// <returns>Ordered browser-safe operator metadata.<br/></returns>
    private static object[] NumericSearchOperators(bool hasComparableField)
    {
        var operators = new List<object>
        {
            new { value = "equal", label = "Equals" }, new { value = "greater", label = "Greater than" },
            new { value = "greaterOrEqual", label = "At least" }, new { value = "less", label = "Less than" },
            new { value = "lessOrEqual", label = "At most" }, new { value = "between", label = "Between" }
        };
        if (hasComparableField)
        {
            operators.AddRange([
                new { value = "propEqual", label = "Equals field" }, new { value = "propNotEqual", label = "Differs from field" },
                new { value = "propGreater", label = "Greater than field" }, new { value = "propGreaterOrEqual", label = "At least field" },
                new { value = "propLess", label = "Less than field" }, new { value = "propLessOrEqual", label = "At most field" }
            ]);
        }
        return operators.ToArray();
    }

    /// <summary>Identifies an allow-listed property-to-property numeric operation.<br/></summary>
    /// <param name="operation">Canonical browser operation token.<br/></param>
    /// <returns>True only for one of Meridian's six explicit property comparison tokens.<br/></returns>
    private static bool IsPropertyComparison(string operation)
        => operation is "propEqual" or "propNotEqual" or "propGreater" or "propGreaterOrEqual" or "propLess" or "propLessOrEqual";

    /// <summary>
    /// Returns the three visible result-reading strategies available for one dataset.<br/>
    /// Each strategy remains ordinary editable UI state: examples may select it, but no example identifier crosses the bridge or changes backend behavior.<br/>
    /// </summary>
    /// <param name="dataset">Allow-listed dataset whose fields define the result columns.<br/></param>
    /// <returns>Complete-object, one-property, and reduced-object modes with concrete columns.<br/></returns>
    private static SearchResultModeDefinition[] SearchResultModes(SearchDatasetDefinition dataset)
    {
        string valueFieldName = dataset.Name switch
        {
            "orders" => "Number", "customers" => "Name", "products" => "Name", "inventory" => "SKU", "lines" => "SKU",
            _ => throw new ArgumentException("Unknown search dataset.")
        };
        string[] projectionFieldNames = dataset.Name switch
        {
            "orders" => ["Number", "Status", "Total"],
            "customers" => ["Code", "Name", "Email"],
            "products" => ["SKU", "Name", "Price"],
            "inventory" => ["SKU", "OnHand", "Reserved"],
            "lines" => ["SKU", "Quantity", "Total"],
            _ => throw new ArgumentException("Unknown search dataset.")
        };
        return
        [
            new("object", "Complete object", "Hydrate the bounded page as complete scalar domain objects; registered relationships remain suppressed.", dataset.Fields.Take(5).ToArray()),
            new("value", "One property", $"Decode only {ResolveField(dataset, valueFieldName).Label} from each matching Inheto payload; no domain object is created.", [ResolveField(dataset, valueFieldName)]),
            new("projection", "Small object", "Project three selected properties into a smaller CLR result type instead of hydrating the complete domain object.", projectionFieldNames.Select(name => ResolveField(dataset, name)).ToArray())
        ];
    }

    /// <summary>Resolves one visible result mode and rejects arbitrary materialization instructions from the browser.<br/></summary>
    /// <param name="dataset">Selected allow-listed dataset.<br/></param>
    /// <param name="name">Browser result-mode token.<br/></param>
    /// <returns>The matching server-owned result shape definition.<br/></returns>
    private static SearchResultModeDefinition ResolveSearchResultMode(SearchDatasetDefinition dataset, string name)
        => SearchResultModes(dataset).FirstOrDefault(mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose complete objects, one property, or a small object result shape.");

    /// <summary>
    /// Projects the current matching payload into the dataset's explicit three-property CLR type through Inheto.<br/>
    /// <c>GetNoLinks&lt;TOut&gt;</c> performs the projection directly from the stored payload; Meridian does not hydrate the source domain object and copy its members afterward.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete source record type owned by the reader.<br/></typeparam>
    /// <param name="reader">Positioned condition reader whose current payload is still borrowed.<br/></param>
    /// <param name="datasetName">Validated dataset selecting one compile-time reduced CLR type.<br/></param>
    /// <returns>The reduced object produced by Inheto from the current payload.<br/></returns>
    private static object ProjectSearchRow<T>(RecordStore<T>.RowReader reader, string datasetName)
        => datasetName switch
        {
            "orders" => reader.GetNoLinks<OrderSearchProjection>(),
            "customers" => reader.GetNoLinks<CustomerSearchProjection>(),
            "products" => reader.GetNoLinks<ProductSearchProjection>(),
            "inventory" => reader.GetNoLinks<InventorySearchProjection>(),
            "lines" => reader.GetNoLinks<OrderLineSearchProjection>(),
            _ => throw new ArgumentException("Unknown search dataset.")
        };

    /// <summary>
    /// Produces the exact public-reader code corresponding to the visible result-shape selection.<br/>
    /// This evidence is separate from the ConditionBuilder code because Abraxas condition construction and Inheto result materialization are independent developer choices.<br/>
    /// </summary>
    /// <param name="dataset">Selected allow-listed dataset.<br/></param>
    /// <param name="resultMode">Validated visible result mode.<br/></param>
    /// <returns>Concise executable-equivalent C# for the selected read shape.<br/></returns>
    private static string BuildResultShapeCode(SearchDatasetDefinition dataset, SearchResultModeDefinition resultMode)
    {
        if (resultMode.Name == "value")
            return $"using var reader = {dataset.StoreVariable}.Readers.OpenValuesWithIds(condition, \".{resultMode.Columns[0].Name}\");\nwhile (reader.Read())\n    var value = reader.Get(0).{SearchAccessorCode(resultMode.Columns[0].Kind)}; // one property; no {dataset.RecordType.Name} object";
        if (resultMode.Name == "projection")
        {
            string projection = dataset.Name switch
            {
                "orders" => nameof(OrderSearchProjection), "customers" => nameof(CustomerSearchProjection),
                "products" => nameof(ProductSearchProjection), "inventory" => nameof(InventorySearchProjection),
                "lines" => nameof(OrderLineSearchProjection), _ => throw new ArgumentException("Unknown search dataset.")
            };
            return $"using var reader = {dataset.StoreVariable}.Readers.Open(condition);\nwhile (reader.ReadNoLinks())\n    {projection} value = reader.GetNoLinks<{projection}>(); // direct payload projection";
        }
        return $"using var reader = {dataset.StoreVariable}.Readers.Open(condition);\nwhile (reader.ReadNoLinks())\n    {dataset.RecordType.Name} value = reader.GetNoLinks();";
    }

    /// <summary>
    /// Decodes one selected value through the explicit typed <see cref="RecordStore{T}.ValueAccessor"/> surface.<br/>
    /// This preserves the narrow reader contract and prevents runtime metadata objects from leaking into Meridian's JSON response.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete source record type.<br/></typeparam>
    /// <param name="reader">Positioned one-property identity-aware value reader.<br/></param>
    /// <param name="kind">Validated scalar family of the selected result property.<br/></param>
    /// <returns>The decoded scalar or null when the stored property is null or missing.<br/></returns>
    private static object? ReadSearchScalar<T>(RecordStore<T>.ValueDataReaderWithIds reader, SearchValueKind kind)
    {
        RecordStore<T>.ValueAccessor value = reader.Get(0);
        return kind switch
        {
            SearchValueKind.Text => value.AsString,
            SearchValueKind.Int32 => value.AsInt32,
            SearchValueKind.UInt64 => value.AsUInt64,
            SearchValueKind.Decimal => value.AsDecimal,
            SearchValueKind.Date => value.AsDateTime,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Maps one result scalar family to the matching allocation-conscious value-accessor member for developer evidence.<br/></summary>
    /// <param name="kind">Validated result scalar family.<br/></param>
    /// <returns>The public <c>ValueAccessor</c> property name.<br/></returns>
    private static string SearchAccessorCode(SearchValueKind kind)
        => kind switch
        {
            SearchValueKind.Text => "AsString",
            SearchValueKind.Int32 => "AsInt32",
            SearchValueKind.UInt64 => "AsUInt64",
            SearchValueKind.Decimal => "AsDecimal",
            SearchValueKind.Date => "AsDateTime",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    /// <summary>
    /// Converts Abraxas's actual planner event into stable browser evidence.<br/>
    /// The route is never inferred merely from index attachment: direct, candidate-with-residual, and Fractal scan labels come from <see cref="RecordStore{T}.ConditionExecutionPlanned"/>.<br/>
    /// </summary>
    /// <param name="plan">Actual planner event raised for the executed condition reader.<br/></param>
    /// <param name="dataset">Dataset used only for human-readable context.<br/></param>
    /// <param name="clauses">Validated clauses used to identify the folded-reversed suffix showcase.<br/></param>
    /// <returns>Serializable route, reason, maintained index names, and leaf diagnostics.<br/></returns>
    private static SearchPlanEvidence BuildSearchPlanEvidence(ConditionExecutionPlanEventArgs plan, SearchDatasetDefinition dataset, SearchClauseRequest[] clauses)
    {
        bool foldedReversedSuffix = dataset.Name == "customers" && clauses.Any(clause => clause.Field == "Email" && NormalizeOperator(SearchValueKind.Text, clause.Operator) == "ends" && clause.IgnoreCase)
            && plan.PlanKind == ConditionExecutionPlanKind.IndexDirect;
        string routeKind = plan.PlanKind switch
        {
            ConditionExecutionPlanKind.IndexDirect => "indexed",
            ConditionExecutionPlanKind.IndexCandidateWithResidual => "hybrid",
            _ => "scan"
        };
        string label = plan.PlanKind switch
        {
            ConditionExecutionPlanKind.IndexDirect => foldedReversedSuffix ? "LibraDex direct · folded-reversed suffix projection" : "LibraDex direct",
            ConditionExecutionPlanKind.IndexCandidateWithResidual => "LibraDex candidates + Fractal residual",
            _ => "Fractal scan + Inheto evaluation"
        };
        string route = plan.PlanKind switch
        {
            ConditionExecutionPlanKind.IndexDirect when foldedReversedSuffix => "LibraDex folded-reversed email projection → matching identities → selected Fractal/Inheto result shape",
            ConditionExecutionPlanKind.IndexDirect => "LibraDex direct condition plan → matching identities → selected Fractal/Inheto result shape",
            ConditionExecutionPlanKind.IndexCandidateWithResidual => "LibraDex candidate identities → Fractal payload residual evaluation → selected Inheto result shape",
            _ => "Fractal record traversal → Inheto condition evaluation → selected Inheto result shape"
        };
        return new SearchPlanEvidence(
            plan.PlanKind.ToString(), routeKind, label, route, plan.Reason, plan.OperationName,
            plan.IndexNames.ToArray(),
            plan.Leaves.Select(leaf => new SearchPlanLeafEvidence(leaf.LeafType.Name, leaf.IndexName, leaf.IndexAvailable, leaf.UsedByIndex, leaf.Reason)).ToArray());
    }

    /// <summary>
    /// Joins user-facing clause metadata with Abraxas's actual per-leaf planner dispositions.<br/>
    /// Ordinal association is explanatory only; the complete native leaf list remains separately available on the plan when one UI date operation expands into multiple engine leaves.<br/>
    /// </summary>
    /// <typeparam name="T">Concrete store entity type.<br/></typeparam>
    /// <param name="store">Open store used to report attached source-index availability separately from actual use.<br/></param>
    /// <param name="dataset">Allow-listed field metadata.<br/></param>
    /// <param name="clauses">Stable depth-first UI clause sequence.<br/></param>
    /// <param name="plan">Actual condition planner event.<br/></param>
    /// <returns>One explanatory evidence item per UI clause.<br/></returns>
    private static SearchClauseEvidence[] BuildSearchClauseEvidence<T>(RecordStore<T> store, SearchDatasetDefinition dataset, SearchClauseRequest[] clauses, ConditionExecutionPlanEventArgs plan)
        => clauses.Select((clause, index) =>
        {
            SearchFieldDefinition field = ResolveField(dataset, clause.Field);
            ConditionExecutionLeafDiagnostic? leaf = index < plan.Leaves.Count ? plan.Leaves[index] : null;
            bool attached = store.TryGetIndex("." + field.Name, out _);
            bool foldedReversed = field.Name == "Email" && NormalizeOperator(field.Kind, clause.Operator) == "ends" && clause.IgnoreCase && leaf?.UsedByIndex == true;
            string label = foldedReversed ? "FOLDED-REVERSED SUFFIX INDEX"
                : leaf?.UsedByIndex == true ? "LIBRADEX INDEX LEAF"
                : plan.PlanKind == ConditionExecutionPlanKind.RecordScan ? "INHETO / FRACTAL SCAN"
                : "INHETO / FRACTAL RESIDUAL";
            return new SearchClauseEvidence(
                field.Name, field.Label, field.Kind.ToString().ToLowerInvariant(), NormalizeOperator(field.Kind, clause.Operator),
                attached, leaf?.UsedByIndex == true, leaf?.IndexName, label,
                leaf?.Reason ?? "See the complete native planner leaf list for this expanded condition.");
        }).ToArray();

    /// <summary>Defines one validated visible result-reading strategy and its exact output columns.<br/></summary>
    private sealed record SearchResultModeDefinition(string Name, string Label, string Description, SearchFieldDefinition[] Columns);

    /// <summary>Contains the actual plan-level evidence raised by Abraxas for one executed condition.<br/></summary>
    private sealed record SearchPlanEvidence(string Kind, string RouteKind, string Label, string Route, string Reason, string Operation, string[] IndexNames, SearchPlanLeafEvidence[] Leaves);

    /// <summary>Contains one unmodified Abraxas leaf-planning disposition in browser-safe form.<br/></summary>
    private sealed record SearchPlanLeafEvidence(string LeafType, string? IndexName, bool IndexAvailable, bool UsedByIndex, string Reason);

    /// <summary>Combines one visible UI clause with its actual planner-use evidence and static source-index availability.<br/></summary>
    private sealed record SearchClauseEvidence(string Field, string FieldLabel, string Type, string Op, bool Indexed, bool UsedByIndex, string? IndexName, string RouteLabel, string Reason);
}

/// <summary>Reduced order result populated directly by Inheto from a matching stored payload.<br/></summary>
internal sealed class OrderSearchProjection
{
    public string Number { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>Reduced customer result populated directly by Inheto from a matching stored payload.<br/></summary>
internal sealed class CustomerSearchProjection
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

/// <summary>Reduced product result populated directly by Inheto from a matching stored payload.<br/></summary>
internal sealed class ProductSearchProjection
{
    public string SKU { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

/// <summary>Reduced inventory result populated directly by Inheto from a matching stored payload.<br/></summary>
internal sealed class InventorySearchProjection
{
    public string SKU { get; set; } = "";
    public int OnHand { get; set; }
    public int Reserved { get; set; }
}

/// <summary>Reduced line result populated directly by Inheto from a matching stored payload.<br/></summary>
internal sealed class OrderLineSearchProjection
{
    public string SKU { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}
