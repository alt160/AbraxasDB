using System;
using System.Collections;
using System.Collections.Generic;
using Inheto;

namespace AbraxasDB
{
    /// <summary>
    /// Selects how an explicit recursive reader responds when one identity is reached more than once.<br/>
    /// Repeated identities include ordinary cycles, a root reached again through a descendant, and multi-parent graph convergence.<br/>
    /// </summary>
    public enum RecursiveCycleHandling
    {
        /// <summary>Skips an identity after its first breadth-first occurrence.<br/></summary>
        Skip = 0,

        /// <summary>Stops preparation with an exception when an identity would be visited more than once.<br/></summary>
        Throw = 1
    }

    public partial class RecordStore<T>
    {
        /// <summary>
        /// Groups explicit recursive reader entry points without making recursion a permanent property-link behavior.<br/>
        /// Opening captures and validates intent only; <c>Prepare()</c> or the first <c>Read()</c> resolves breadth-first identity topology through the ordinary condition planner.<br/>
        /// </summary>
        public readonly struct RecursiveReaderOperations
        {
            private readonly RecordStore<T> store;

            internal RecursiveReaderOperations(RecordStore<T> store) => this.store = store;

            /// <summary>
            /// Opens a quickly returned recursive row reader rooted at one store-owned identity.<br/>
            /// Preparation includes the root at depth zero, then finds records whose parent property equals each prior breadth-first frontier; a compatible LibraDex index is adopted automatically and otherwise the existing authoritative fallback is used.<br/>
            /// Row payloads remain unmaterialized until a current-row property or object getter is called.<br/>
            /// </summary>
            /// <param name="rootID">Store-owned identity that begins the traversal.<br/></param>
            /// <param name="parentPath">UInt64 or nullable-UInt64 property path containing each record's parent identity.<br/></param>
            /// <param name="maxDepth">Maximum included descendant depth; zero includes only the root.<br/></param>
            /// <param name="cycleHandling">Behavior when an identity is reached after its first breadth-first occurrence.<br/></param>
            /// <returns>An unprepared recursive row reader positioned before its root.<br/></returns>
            public RecursiveRowReader Open(
                ulong rootID,
                string parentPath,
                int maxDepth = int.MaxValue,
                RecursiveCycleHandling cycleHandling = RecursiveCycleHandling.Skip)
                => new(store, rootID, parentPath, maxDepth, cycleHandling);

            /// <summary>
            /// Opens a quickly returned payload-free recursive identity reader rooted at one store-owned identity.<br/>
            /// <c>Prepare()</c> or the first <c>Read()</c> resolves the same breadth-first topology as <see cref="Open(ulong, string, int, RecursiveCycleHandling)"/> while retaining parent and depth metadata for each identity.<br/>
            /// </summary>
            /// <param name="rootID">Store-owned identity that begins the traversal.<br/></param>
            /// <param name="parentPath">UInt64 or nullable-UInt64 property path containing each record's parent identity.<br/></param>
            /// <param name="maxDepth">Maximum included descendant depth; zero includes only the root.<br/></param>
            /// <param name="cycleHandling">Behavior when an identity is reached after its first breadth-first occurrence.<br/></param>
            /// <returns>An unprepared recursive identity reader positioned before its root.<br/></returns>
            public RecursiveIdentityReader OpenIdentities(
                ulong rootID,
                string parentPath,
                int maxDepth = int.MaxValue,
                RecursiveCycleHandling cycleHandling = RecursiveCycleHandling.Skip)
                => new(store, rootID, parentPath, maxDepth, cycleHandling);

            /// <summary>
            /// Opens a quickly returned recursive reader for one typed property value without hydrating complete record objects.<br/>
            /// Topology preparation reads identities and parent keys only; the selected value is decoded lazily for each current row when <c>Get()</c> is called.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact requested scalar value type.<br/></typeparam>
            /// <param name="rootID">Store-owned identity that begins the traversal.<br/></param>
            /// <param name="parentPath">UInt64 or nullable-UInt64 property path containing each record's parent identity.<br/></param>
            /// <param name="valuePath">Property path decoded for each current recursive row.<br/></param>
            /// <param name="maxDepth">Maximum included descendant depth; zero includes only the root.<br/></param>
            /// <param name="cycleHandling">Behavior when an identity is reached after its first breadth-first occurrence.<br/></param>
            /// <returns>An unprepared recursive typed-value reader positioned before its root.<br/></returns>
            public RecursiveValueReader<TValue> OpenValues<TValue>(
                ulong rootID,
                string parentPath,
                string valuePath,
                int maxDepth = int.MaxValue,
                RecursiveCycleHandling cycleHandling = RecursiveCycleHandling.Skip)
                => new(store, rootID, parentPath, valuePath, maxDepth, cycleHandling);
        }

        /// <summary>
        /// Provides breadth-first recursive row traversal with topology metadata and demand-driven current-row materialization.<br/>
        /// The reader owns its prepared identity order and the underlying ordinary Abraxas row reader used after topology resolution.<br/>
        /// </summary>
        public sealed class RecursiveRowReader : IDisposable
        {
            private readonly RecordStore<T> store;
            private readonly RecursiveTraversalPlan traversal;
            private RecursiveNode[]? nodes;
            private RowReader? rows;
            private int nodeIndex = -1;
            private bool disposed;

            internal RecursiveRowReader(
                RecordStore<T> store,
                ulong rootID,
                string parentPath,
                int maxDepth,
                RecursiveCycleHandling cycleHandling)
            {
                this.store = store;
                traversal = new RecursiveTraversalPlan(store, rootID, parentPath, maxDepth, cycleHandling);
            }

            /// <summary>Gets whether topology has been resolved and the ordinary row cursor has been opened.<br/></summary>
            public bool IsPrepared => nodes is not null;

            /// <summary>Gets the current record identity without materializing its payload.<br/></summary>
            public ulong ID => RequireRows().ID;

            /// <summary>Gets the identity from which the current row was first reached, or null for the root row.<br/></summary>
            public ulong? ParentID => RequireCurrentNode().ParentID;

            /// <summary>Gets the root-relative breadth-first depth of the current row; the root is depth zero.<br/></summary>
            public int Depth => RequireCurrentNode().Depth;

            /// <summary>Gets the zero-based ordinal among live prepared rows, or -1 before the first successful read.<br/></summary>
            public long Ordinal => rows?.Ordinal ?? -1;

            /// <summary>
            /// Resolves the complete bounded identity topology without hydrating record objects and opens the ordinary row cursor over that prepared order.<br/>
            /// Repeated calls are no-ops; an absent root prepares an empty reader.<br/>
            /// </summary>
            public void Prepare()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (nodes is not null)
                    return;
                RecursiveNode[] prepared = traversal.Prepare();
                RowReader preparedRows = store.Readers.Open(new RecursiveIdentityCollection(prepared));
                nodes = prepared;
                rows = preparedRows;
            }

            /// <summary>
            /// Advances to the next breadth-first row, preparing topology synchronously when <see cref="Prepare"/> was not called explicitly.<br/>
            /// No object is materialized by movement alone.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current-row getters and topology metadata are available.<br/></returns>
            public bool Read()
            {
                Prepare();
                return Advance(RequireRows().Read());
            }

            /// <summary>
            /// Advances to the next breadth-first row while suppressing all registered property links during later current-row materialization.<br/>
            /// Topology preparation itself never invokes property-link deserializers.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current-row getters and topology metadata are available.<br/></returns>
            public bool ReadNoLinks()
            {
                Prepare();
                return Advance(RequireRows().ReadNoLinks());
            }

            /// <summary>
            /// Advances to the next breadth-first row while suppressing only the named registered links during later current-row materialization.<br/>
            /// Unlisted links preserve ordinary reader behavior.<br/>
            /// </summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns><see langword="true"/> when current-row getters and topology metadata are available.<br/></returns>
            public bool ReadNoLinks(string link, params string[] additionalLinks)
            {
                Prepare();
                return Advance(RequireRows().ReadNoLinks(link, additionalLinks));
            }

            /// <summary>Advances to the next breadth-first row, preparing topology on first use.<br/></summary>
            /// <returns><see langword="true"/> when positioned on a live prepared row.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Gets the current payload as the store's declared record shape and honors registered property links.<br/></summary>
            /// <returns>The materialized current record.<br/></returns>
            public T Get() => RequireRows().Get();

            /// <summary>Gets the current payload as an alternate projection shape and honors matching registered property links.<br/></summary>
            /// <typeparam name="TOut">Requested object or projection shape.<br/></typeparam>
            /// <returns>The materialized current projection.<br/></returns>
            public TOut GetAs<TOut>() => RequireRows().GetAs<TOut>();

            /// <summary>Reads one typed current-row property through ordinary index, link, or Inheto semantics.<br/></summary>
            /// <typeparam name="TValue">Requested property type.<br/></typeparam>
            /// <param name="propPath">Property path to read.<br/></param>
            /// <returns>The current value, including null or default where its property semantics permit.<br/></returns>
            public TValue? Get<TValue>(string propPath) => RequireRows().Get<TValue>(propPath);

            /// <summary>Reads one current-row property through its runtime value shape.<br/></summary>
            /// <param name="propPath">Property path to read.<br/></param>
            /// <returns>The runtime value, or null for null or missing data.<br/></returns>
            public object? Get(string propPath) => RequireRows().Get(propPath);

            /// <summary>Materializes the current record while suppressing every registered property link.<br/></summary>
            /// <returns>The current store record with linked members left at their defaults.<br/></returns>
            public T GetNoLinks() => RequireRows().GetNoLinks();

            /// <summary>Reprojects the current record while suppressing every registered property link.<br/></summary>
            /// <typeparam name="TOut">Requested object or projection shape.<br/></typeparam>
            /// <returns>The current projection with linked members left at their defaults.<br/></returns>
            public TOut GetNoLinks<TOut>() => RequireRows().GetNoLinks<TOut>();

            /// <summary>Releases the prepared row cursor and its store-operation lease.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                nodeIndex = -1;
                rows?.Dispose();
                rows = null;
            }

            private bool Advance(bool moved)
            {
                if (!moved)
                {
                    nodeIndex = -1;
                    return false;
                }

                RecursiveNode[] prepared = nodes!;
                ulong currentID = rows!.ID;
                while (++nodeIndex < prepared.Length && prepared[nodeIndex].ID != currentID)
                {
                }
                if ((uint)nodeIndex >= (uint)prepared.Length)
                    throw new InvalidOperationException("The recursive row cursor returned an identity outside its prepared topology.");
                return true;
            }

            private RowReader RequireRows()
                => rows ?? throw new InvalidOperationException("The recursive reader is not prepared or has been disposed.");

            private RecursiveNode RequireCurrentNode()
            {
                RecursiveNode[] prepared = nodes ?? throw new InvalidOperationException("The recursive reader is not prepared.");
                if ((uint)nodeIndex >= (uint)prepared.Length)
                    throw new InvalidOperationException("The recursive reader is not positioned on a row.");
                return prepared[nodeIndex];
            }
        }

        /// <summary>
        /// Provides payload-free breadth-first recursive identity traversal with parent and depth metadata.<br/>
        /// The prepared topology is a stable snapshot of identities resolved at preparation time.<br/>
        /// </summary>
        public sealed class RecursiveIdentityReader : IDisposable
        {
            private readonly RecursiveTraversalPlan traversal;
            private RecursiveNode[]? nodes;
            private int index = -1;
            private bool disposed;

            internal RecursiveIdentityReader(
                RecordStore<T> store,
                ulong rootID,
                string parentPath,
                int maxDepth,
                RecursiveCycleHandling cycleHandling)
                => traversal = new RecursiveTraversalPlan(store, rootID, parentPath, maxDepth, cycleHandling);

            /// <summary>Gets whether the complete bounded topology has been resolved.<br/></summary>
            public bool IsPrepared => nodes is not null;

            /// <summary>Gets the current recursive identity without reading its record payload.<br/></summary>
            public ulong ID => RequireCurrent().ID;

            /// <summary>Gets the identity from which the current identity was first reached, or null for the root.<br/></summary>
            public ulong? ParentID => RequireCurrent().ParentID;

            /// <summary>Gets the root-relative breadth-first depth of the current identity.<br/></summary>
            public int Depth => RequireCurrent().Depth;

            /// <summary>Gets the zero-based current topology ordinal, or -1 before the first successful read.<br/></summary>
            public long Ordinal => index;

            /// <summary>
            /// Resolves the complete bounded identity topology without hydrating any record object.<br/>
            /// Repeated calls are no-ops and an absent root produces an empty topology.<br/>
            /// </summary>
            public void Prepare()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                nodes ??= traversal.Prepare();
            }

            /// <summary>
            /// Advances to the next breadth-first identity, preparing topology synchronously on first use.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when identity and topology metadata are available.<br/></returns>
            public bool Read()
            {
                Prepare();
                int next = index + 1;
                if ((uint)next >= (uint)nodes!.Length)
                {
                    index = nodes.Length;
                    return false;
                }
                index = next;
                return true;
            }

            /// <summary>Advances to the next breadth-first identity, preparing topology on first use.<br/></summary>
            /// <returns><see langword="true"/> when positioned on an identity.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Gets the current recursive identity without resolving its payload.<br/></summary>
            /// <returns>The current store-owned identity.<br/></returns>
            public ulong Get() => RequireCurrent().ID;

            /// <summary>Releases this reader's prepared topology for collection.<br/></summary>
            public void Dispose()
            {
                disposed = true;
                nodes = null;
                index = -1;
            }

            private RecursiveNode RequireCurrent()
            {
                RecursiveNode[] prepared = nodes ?? throw new InvalidOperationException("The recursive identity reader is not prepared.");
                if ((uint)index >= (uint)prepared.Length)
                    throw new InvalidOperationException("The recursive identity reader is not positioned on a result.");
                return prepared[index];
            }
        }

        /// <summary>
        /// Provides one typed value per breadth-first recursive row while retaining identity, parent, and depth metadata.<br/>
        /// The selected value is decoded only when <see cref="Get"/> is called for the current row.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact requested scalar value type.<br/></typeparam>
        public sealed class RecursiveValueReader<TValue> : IDisposable
        {
            private readonly RecursiveRowReader rows;
            private readonly string valuePath;

            internal RecursiveValueReader(
                RecordStore<T> store,
                ulong rootID,
                string parentPath,
                string valuePath,
                int maxDepth,
                RecursiveCycleHandling cycleHandling)
            {
                this.valuePath = ValidateRecursiveValuePath<TValue>(valuePath);
                rows = new RecursiveRowReader(store, rootID, parentPath, maxDepth, cycleHandling);
            }

            /// <summary>Gets whether topology has been resolved and the underlying row cursor has been opened.<br/></summary>
            public bool IsPrepared => rows.IsPrepared;

            /// <summary>Gets the current record identity without materializing its object shape.<br/></summary>
            public ulong ID => rows.ID;

            /// <summary>Gets the identity from which the current value row was first reached, or null for the root.<br/></summary>
            public ulong? ParentID => rows.ParentID;

            /// <summary>Gets the root-relative breadth-first depth of the current value row.<br/></summary>
            public int Depth => rows.Depth;

            /// <summary>Gets the zero-based current live-row ordinal, or -1 before the first successful read.<br/></summary>
            public long Ordinal => rows.Ordinal;

            /// <summary>Resolves bounded topology without decoding the selected value or hydrating record objects.<br/></summary>
            public void Prepare() => rows.Prepare();

            /// <summary>Advances to the next breadth-first value row, preparing topology on first use.<br/></summary>
            /// <returns><see langword="true"/> when <see cref="Get"/> and topology metadata are available.<br/></returns>
            public bool Read() => rows.Read();

            /// <summary>Advances while suppressing every registered link, including a linked selected value path.<br/></summary>
            /// <returns><see langword="true"/> when <see cref="Get"/> and topology metadata are available.<br/></returns>
            public bool ReadNoLinks() => rows.ReadNoLinks();

            /// <summary>Advances while suppressing only named links during a later selected-value read.<br/></summary>
            /// <param name="link">First registered link property path to suppress.<br/></param>
            /// <param name="additionalLinks">Optional additional registered link paths to suppress.<br/></param>
            /// <returns><see langword="true"/> when <see cref="Get"/> and topology metadata are available.<br/></returns>
            public bool ReadNoLinks(string link, params string[] additionalLinks)
                => rows.ReadNoLinks(link, additionalLinks);

            /// <summary>Advances to the next breadth-first value row, preparing topology on first use.<br/></summary>
            /// <returns><see langword="true"/> when positioned on a row.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Decodes the selected current-row value through ordinary index, link, or Inheto semantics.<br/></summary>
            /// <returns>The current selected value, including null or default where its property semantics permit.<br/></returns>
            public TValue? Get() => rows.Get<TValue>(valuePath);

            /// <summary>Releases the prepared row cursor and its store-operation lease.<br/></summary>
            public void Dispose() => rows.Dispose();
        }

        private sealed class RecursiveTraversalPlan
        {
            private readonly RecordStore<T> store;
            private readonly ulong rootID;
            private readonly string parentPath;
            private readonly int maxDepth;
            private readonly RecursiveCycleHandling cycleHandling;

            internal RecursiveTraversalPlan(
                RecordStore<T> store,
                ulong rootID,
                string parentPath,
                int maxDepth,
                RecursiveCycleHandling cycleHandling)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                store.EnsureRecordBelongsToCurrentStore(rootID, nameof(rootID));
                if (maxDepth < 0)
                    throw new ArgumentOutOfRangeException(nameof(maxDepth));
                if (!Enum.IsDefined(cycleHandling))
                    throw new ArgumentOutOfRangeException(nameof(cycleHandling));

                this.rootID = rootID;
                this.parentPath = ValidateRecursiveParentPath(parentPath);
                this.maxDepth = maxDepth;
                this.cycleHandling = cycleHandling;
            }

            internal RecursiveNode[] Prepare()
            {
                if (!store.Exists(rootID))
                    return Array.Empty<RecursiveNode>();

                var nodes = new List<RecursiveNode>(16) { new(rootID, null, 0) };
                var visited = new HashSet<ulong> { rootID };
                var frontier = new List<ulong>(1) { rootID };
                ConditionParameter<IEnumerable<ulong>> frontierParameter = store.Parameter<ulong>(
                    Array.Empty<ulong>(),
                    "recursive-frontier");
                Condition<T>.EndCondition childrenOfFrontier = store.Where
                    .PropPath(parentPath)
                    .AsUInt64
                    .IsIn(frontierParameter)
                    .EndCondition;
                int depth = 0;

                while (depth < maxDepth && frontier.Count != 0)
                {
                    frontierParameter.Value = frontier;
                    var next = new List<ulong>();
                    using RowReader children = store.Readers.Open(childrenOfFrontier);
                    while (children.Read())
                    {
                        ulong childID = children.ID;
                        ulong parentID = children.Get<ulong>(parentPath);
                        if (!visited.Add(childID))
                        {
                            if (cycleHandling == RecursiveCycleHandling.Throw)
                            {
                                throw new InvalidOperationException(
                                    $"Recursive traversal reached identity '{childID}' more than once from parent '{parentID}' at depth '{depth + 1}'.");
                            }
                            continue;
                        }

                        nodes.Add(new RecursiveNode(childID, parentID, depth + 1));
                        next.Add(childID);
                    }

                    frontier = next;
                    depth++;
                }

                return nodes.ToArray();
            }
        }

        private readonly struct RecursiveNode
        {
            internal RecursiveNode(ulong id, ulong? parentID, int depth)
            {
                ID = id;
                ParentID = parentID;
                Depth = depth;
            }

            internal ulong ID { get; }
            internal ulong? ParentID { get; }
            internal int Depth { get; }
        }

        private sealed class RecursiveIdentityCollection : IReadOnlyCollection<ulong>
        {
            private readonly RecursiveNode[] nodes;

            internal RecursiveIdentityCollection(RecursiveNode[] nodes)
                => this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));

            public int Count => nodes.Length;

            public IEnumerator<ulong> GetEnumerator()
            {
                for (var i = 0; i < nodes.Length; i++)
                    yield return nodes[i].ID;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static string ValidateRecursiveParentPath(string parentPath)
        {
            PropPathCheck check = InhetoBinary.CheckPropPath(typeof(T), parentPath);
            if (!check.IsValid || check.ResultType is null)
                throw new ArgumentException($"Recursive parent path '{parentPath}' is not valid for '{typeof(T).FullName}'.", nameof(parentPath));
            Type actual = Nullable.GetUnderlyingType(check.ResultType) ?? check.ResultType;
            if (actual != typeof(ulong))
            {
                throw new InvalidCastException(
                    $"Recursive parent path '{check.CanonicalPath}' resolves to '{check.ResultType.FullName}', not UInt64 or nullable UInt64.");
            }
            return check.CanonicalPath;
        }

        private static string ValidateRecursiveValuePath<TValue>(string valuePath)
        {
            PropPathCheck check = InhetoBinary.CheckPropPath(typeof(T), valuePath);
            if (!check.IsValid || check.ResultType is null)
                throw new ArgumentException($"Recursive value path '{valuePath}' is not valid for '{typeof(T).FullName}'.", nameof(valuePath));
            Type actual = Nullable.GetUnderlyingType(check.ResultType) ?? check.ResultType;
            Type requested = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
            if (actual != requested)
            {
                throw new InvalidCastException(
                    $"Recursive value path '{check.CanonicalPath}' resolves to '{check.ResultType.FullName}', not requested '{typeof(TValue).FullName}'.");
            }
            return check.CanonicalPath;
        }
    }
}
