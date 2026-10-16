using LibraDex;

namespace AbraxasDB;

/// <summary>
/// Selects how a bound Abraxas record store should use LibraDex for supported index queries.<br/>
/// Preferred execution falls back to authoritative Fractal/Inheto evaluation when no compatible maintained index can preserve the requested semantics.<br/>
/// </summary>
internal enum LibraDexIndexBackendMode
{
    /// <summary>
    /// Disables LibraDex query/index execution and evaluates supported conditions from authoritative Fractal/Inheto records.<br/>
    /// </summary>
    FractalOnly = 0,
    /// <summary>
    /// Prefers compatible maintained LibraDex indexes, then uses safe candidate reduction or authoritative Fractal/Inheto evaluation as required.<br/>
    /// </summary>
    Preferred = 1,
    /// <summary>
    /// Require LibraDex execution for supported public query calls and fail when the condition or index shape is not mapped.<br/>
    /// </summary>
    Required = 2,
    /// <summary>
    /// Compatibility alias for <see cref="Preferred"/>.<br/>
    /// </summary>
    [Obsolete("Use Preferred.")]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    LibraDexPreferred = Preferred,
    /// <summary>
    /// Compatibility alias for <see cref="Required"/>.<br/>
    /// </summary>
    [Obsolete("Use Required.")]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    LibraDexRequired = Required
}

/// <summary>
/// Provides Abraxas-owned binding and direct identity-query entry points for a LibraDex identity group.<br/>
/// Bound RecordStore execution retains Fractal/Inheto as authoritative payload storage while LibraDex supplies compatible direct or candidate identity plans.<br/>
/// </summary>
internal static class LibraDexIdentityQuery
{
    /// <summary>
    /// Binds an Abraxas record store to a LibraDex identity group for supported internal query execution.<br/>
    /// Existing record-store query calls remain unchanged; unsupported conditions continue through authoritative Fractal/Inheto evaluation.<br/>
    /// </summary>
    /// <typeparam name="TRecord">The Abraxas record type owned by the store.</typeparam>
    /// <param name="store">The record store whose stable public query methods should attempt LibraDex execution first.<br/></param>
    /// <param name="indexes">The LibraDex identity group indexes that mirror the store's record identifiers.<br/></param>
    public static void Bind<TRecord>(RecordStore<TRecord> store, CatalogIdentityGroupIndexes indexes)
        => Bind(store, indexes, LibraDexIndexBackendMode.Preferred);

    /// <summary>
    /// Binds an Abraxas record store to a LibraDex identity group with an explicit backend mode.<br/>
    /// Existing record-store query calls remain unchanged; the mode controls whether unsupported LibraDex conditions fall back to Fractal/Inheto or fail fast for deterministic native-execution proof.<br/>
    /// </summary>
    /// <typeparam name="TRecord">The Abraxas record type owned by the store.</typeparam>
    /// <param name="store">The record store whose stable public query methods should attempt LibraDex execution.<br/></param>
    /// <param name="indexes">The LibraDex identity group indexes that mirror the store's record identifiers.<br/></param>
    /// <param name="mode">The backend selection mode for supported RecordStore query calls.<br/></param>
    public static void Bind<TRecord>(RecordStore<TRecord> store, CatalogIdentityGroupIndexes indexes, LibraDexIndexBackendMode mode)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(indexes);
        RequireUInt64Identities(indexes);
        store.BindLibraDexIdentityIndexes(indexes, mode);
    }

    /// <summary>
    /// Clears a previous LibraDex identity-query binding from an Abraxas record store.<br/>
    /// After clearing, existing public query methods evaluate supported conditions from authoritative Fractal/Inheto records.<br/>
    /// </summary>
    /// <typeparam name="TRecord">The Abraxas record type owned by the store.</typeparam>
    /// <param name="store">The record store whose LibraDex binding should be cleared.<br/></param>
    public static void Clear<TRecord>(RecordStore<TRecord> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.BindLibraDexIdentityIndexes(null, LibraDexIndexBackendMode.FractalOnly);
    }

    /// <summary>
    /// Creates a LibraDex identity-query adapter from an AbraxasDB call site.<br/>
    /// The adapter is bound to one LibraDex catalog identity group and returns only identities, allowing Abraxas to hydrate records through its existing record-store path after the query boundary.<br/>
    /// </summary>
    /// <typeparam name="TIdentity">The caller-owned identity type returned by the adapter.</typeparam>
    /// <param name="indexes">The LibraDex identity group indexes to query.</param>
    /// <returns>A LibraDex identity-only query adapter bound to <paramref name="indexes"/>.</returns>
    public static AbraxasIdentityQueryAdapter<TIdentity> For<TIdentity>(CatalogIdentityGroupIndexes indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        RequireUInt64Identities(indexes);
        return indexes.AbraxasIdentityQuery<TIdentity>();
    }

    /// <summary>
    /// Verifies that an Abraxas-bound LibraDex index group belongs to a catalog constrained to UInt64 scalar identities.<br/>
    /// FractalKVS provides UInt64 record identifiers, so Abraxas must reject unconstrained or differently constrained LibraDex catalogs before record IDs are mirrored into indexes.<br/>
    /// </summary>
    /// <param name="indexes">The LibraDex identity group indexes proposed for Abraxas binding.<br/></param>
    private static void RequireUInt64Identities(CatalogIdentityGroupIndexes indexes)
    {
        if (indexes.RequiredIdentityType != typeof(ulong))
        {
            throw new InvalidOperationException("Abraxas requires LibraDex catalogs created or opened with CatalogOptions.UInt64Identities.");
        }
    }

    /// <summary>
    /// Materializes caller-owned identities for a completed LibraDex condition from an AbraxasDB call site.<br/>
    /// This helper keeps the first integration proof low-friction while preserving the same execution path as <see cref="For{TIdentity}"/> and LibraDex's catalog-bound adapter.<br/>
    /// </summary>
    /// <typeparam name="TIdentity">The caller-owned identity type returned by the query.</typeparam>
    /// <param name="indexes">The LibraDex identity group indexes to query.</param>
    /// <param name="condition">The completed LibraDex condition descriptor to execute.</param>
    /// <param name="ordering">The requested identity ordering contract.</param>
    /// <param name="deduplication">The requested duplicate identity policy.</param>
    /// <param name="skip">The number of matching identities to skip.</param>
    /// <param name="take">The optional maximum number of identities to return.</param>
    /// <param name="bookmark">The optional continuation bookmark.</param>
    /// <returns>A typed list of caller-owned identities.</returns>
    public static IReadOnlyList<TIdentity> Get<TIdentity>(
        CatalogIdentityGroupIndexes indexes,
        LibraDexConditionEndCondition condition,
        IdentityResultOrdering ordering = IdentityResultOrdering.PlanNatural,
        IdentityDeduplication deduplication = IdentityDeduplication.Distinct,
        int skip = 0,
        int? take = null,
        LibraDexBookmark? bookmark = null)
    {
        return For<TIdentity>(indexes).Get(condition, ordering, deduplication, skip, take, bookmark);
    }

    /// <summary>
    /// Opens a forward-only cursor over caller-owned identities for a completed LibraDex condition from an AbraxasDB call site.<br/>
    /// The returned cursor is LibraDex's native identity cursor, so this binding adds no per-row delegate or wrapper in the traversal path.<br/>
    /// </summary>
    /// <typeparam name="TIdentity">The caller-owned identity type returned by the cursor.</typeparam>
    /// <param name="indexes">The LibraDex identity group indexes to query.</param>
    /// <param name="condition">The completed LibraDex condition descriptor to execute.</param>
    /// <param name="ordering">The requested identity ordering contract.</param>
    /// <param name="deduplication">The requested duplicate identity policy.</param>
    /// <param name="skip">The number of matching identities to skip.</param>
    /// <param name="take">The optional maximum number of identities to return.</param>
    /// <param name="bookmark">The optional continuation bookmark.</param>
    /// <returns>A forward-only cursor over caller-owned identities.</returns>
    public static LibraDexIdentityCursor<TIdentity> OpenCursor<TIdentity>(
        CatalogIdentityGroupIndexes indexes,
        LibraDexConditionEndCondition condition,
        IdentityResultOrdering ordering = IdentityResultOrdering.PlanNatural,
        IdentityDeduplication deduplication = IdentityDeduplication.Distinct,
        int skip = 0,
        int? take = null,
        LibraDexBookmark? bookmark = null)
    {
        return For<TIdentity>(indexes).OpenCursor(condition, ordering, deduplication, skip, take, bookmark);
    }
}
