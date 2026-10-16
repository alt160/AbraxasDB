using LibraDex;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    /// <summary>Recognizes complete ordinary scalar maintenance, including its missing/null and empty contributions.<br/>
    /// Unlike State, this source represents every store record; custom, calculated and collection membership sources decline.<br/></summary>
    private static bool IsCompleteScalarCountIndex(LibraDexSimpleIndex index)
        => (index.KeyType == typeof(string) || index.KeyType.IsValueType) &&
            index.CustomKeyReader is null && index.CustomPayloadReader is null &&
            index.CalculatedSelector is null && index.CalculatedReader is null && index.FunctionRuntimeReader is null &&
            !index.IsFunctionIndex && !index.IsVirtualProperty && index.IncludeNullValues && index.IncludeEmptyStrings &&
            index.Index is IIdentityPrimitiveExecutor;

    /// <summary>Counts a complete persistent ordinary index without decoding identities or using optional State as a universe.<br/>
    /// Stores without a proven complete source retain their authoritative Fractal count.<br/></summary>
    private bool TryCountAllRecordsByIndex(out ulong count)
    {
        count = 0;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly) return false;
        EnsureLibraDexConvergence(nameof(TryCountAllRecordsByIndex));
        foreach (var index in libraDexSimpleIndexes)
        {
            if (index.OwnerCatalog is not null || !IsCompleteScalarCountIndex(index)) continue;
            count = checked((ulong)((IIdentityPrimitiveExecutor)index.Index).CountIdentityPrimitive(
                new(LibraDexCriteriaKind.All, Array.Empty<object?>())));
            return true;
        }
        return false;
    }

    /// <summary>Attempts native record counting using the existing complete semantic condition translation.<br/>
    /// Persistent and volatile owners remain separate; unsupported and residual plans keep authoritative evaluation.<br/></summary>
    private bool TryCountNativeCondition(Condition<T>.EndCondition where, out ulong count)
    {
        using var operation = EnterStoreOperationScope();
        count = 0;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly) return false;
        EnsureLibraDexConvergence(nameof(TryCountNativeCondition));
        if (libraDexIdentityIndexes is not null && TryCountNativeCondition(where, libraDexIdentityIndexes, null, out count)) return true;
        return HasVolatileLibraDexIndexes() && TryCountNativeCondition(where, LibraDexVolatileIndexes, libraDexVolatileCatalog, out count);
    }

    /// <summary>Translates against one catalog using the normal alias, derived-selector and reserved-State rules.<br/></summary>
    private bool TryCountNativeCondition(Condition<T>.EndCondition where, CatalogIdentityGroupIndexes group, Catalog? owner, out ulong count)
    {
        count = 0;
        if (!where.builder.TryBuildLibraDexConditionTree(group,
            name => CanUseLibraDexIndexForSemanticPath(name, owner),
            (Condition<T>.Builder builder, IBackendNeutralSelectorDescriptor descriptor, out string name) => TryResolveLibraDexDerivedSelector(builder, descriptor, owner, out name),
            (string logical, out string physical) => TryResolveReservedLibraDexIndexAlias(logical, owner, out physical), out var condition)) return false;
        return TryCountNativeCondition(group, condition, owner, out count);
    }

    /// <summary>Proves the materialized leaf's maintained scalar source before asking LibraDex for a count-only plan.<br/>
    /// Multiple condition leaves are deliberately not summed: overlapping OR and cross-index sets require distinct identities.<br/></summary>
    private bool TryCountNativeCondition(CatalogIdentityGroupIndexes group, LibraDexConditionEndCondition condition, Catalog? owner, out ulong count)
    {
        count = 0;
        if (condition.Leaves.Count != 1) return false;
        string name = condition.Leaves[0].IndexName;
        bool proven = owner is null && libraDexStateIndex is not null && name == LibraDexStateIndexName;
        foreach (var index in libraDexSimpleIndexes)
        {
            if (ReferenceEquals(index.OwnerCatalog, owner) && (index.Path == name || index.Index.Name == name) &&
                (IsCompleteScalarCountIndex(index) || IsCalculatedScalarCountIndex(index)))
            { proven = true; break; }
        }
        if (!group.TryCountMaintainedScalarCondition(condition, proven, out long native)) return false;
        count = checked((ulong)native);
        return true;
    }

    /// <summary>Counts non-null direct scalar projections through native all/null counts instead of tuple enumeration.<br/>
    /// A filtered projection can reuse record counting only when the entire maintained projection has no missing values.<br/>
    /// Otherwise the existing filtered tuple or payload path preserves projection semantics.<br/></summary>
    private bool TryCountNativeProjection(string path, Type type, Condition<T>.EndCondition? where, bool distinct, out ulong count)
    {
        using var operation = EnterStoreOperationScope();
        count = 0;
        if (distinct || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly || NativeDeserializationOptions.Aliases.Contains(path)) return false;
        EnsureLibraDexConvergence(nameof(TryCountNativeProjection));
        type = Nullable.GetUnderlyingType(type) ?? type;
        foreach (var index in libraDexSimpleIndexes)
        {
            if (index.Path != path || index.KeyType != type || !IsCompleteScalarCountIndex(index)) continue;
            return TryCountNativeProjection(index, where, out count);
        }
        return false;
    }

    /// <summary>Admits single-result calculated descriptors only when their maintained index retains null and empty contributions.<br/>
    /// This does not admit arbitrary custom codecs or collection enumeration indexes.<br/></summary>
    private static bool IsCalculatedScalarCountIndex(LibraDexSimpleIndex index)
        => (index.KeyType == typeof(string) || index.KeyType.IsValueType) && index.CalculatedSelector is not null &&
            index.CalculatedReader is not null && index.IncludeNullValues && index.IncludeEmptyStrings &&
            index.CustomKeyReader is null && index.CustomPayloadReader is null && index.Index is IIdentityPrimitiveExecutor;

    /// <summary>Resolves a selector to an exact direct path or matching maintained calculation fingerprint.<br/>
    /// Unmatched named/function selectors retain their semantic payload reader rather than trusting a similar physical name.<br/></summary>
    private bool TryCountNativeSelector(ISelector selector, Condition<T>.EndCondition? where, bool distinct, out ulong count)
    {
        using var operation = EnterStoreOperationScope();
        count = 0;
        if (distinct || libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            selector is not ISelectorStructure { ResultType: not null } structure) return false;
        if (structure.SemanticDescriptor is DirectPropertySelectorDescriptor direct)
            return TryCountNativeProjection(direct.PropertyPath, structure.ResultType, where, false, out count);
        EnsureLibraDexConvergence(nameof(TryCountNativeSelector));
        string fingerprint;
        try { fingerprint = SelectorDescriptor<T>.FromSelector(selector).SemanticFingerprint; }
        catch (NotSupportedException) { return false; }
        foreach (var index in libraDexSimpleIndexes)
            if (index.KeyType == (Nullable.GetUnderlyingType(structure.ResultType) ?? structure.ResultType) &&
                index.CalculatedFingerprint == fingerprint && IsCalculatedScalarCountIndex(index))
                return TryCountNativeProjection(index, where, out count);
        return false;
    }

    /// <summary>Subtracts only the null route from native physical tuple counts; empty strings remain contributing values.<br/>
    /// With nulls present, a filtered projection declines unless the existing semantic traversal handles it.<br/></summary>
    private bool TryCountNativeProjection(LibraDexSimpleIndex index, Condition<T>.EndCondition? where, out ulong count)
    {
        count = 0;
        var executor = (IIdentityPrimitiveExecutor)index.Index;
        long missing = executor.CountIdentityPrimitive(index.KeyType == typeof(string)
            ? new(LibraDexCriteriaKind.KeyState, new object?[] { NullKey.Null })
            : new(LibraDexCriteriaKind.ScalarNull, new object?[] { ScalarNull.Null }));
        if (where is not null)
        {
            if (missing != 0) return false;
            count = CountWhere(where);
            return true;
        }
        long all = executor.CountIdentityPrimitive(new(LibraDexCriteriaKind.All, Array.Empty<object?>()));
        count = checked((ulong)(all - missing));
        return true;
    }
}
