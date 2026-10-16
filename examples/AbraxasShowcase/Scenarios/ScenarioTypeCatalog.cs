namespace AbraxasShowcase;

/// <summary>One persisted learner-facing domain type shown by the Showcase type reference.<br/></summary>
/// <param name="Name">Exact C# identifier used by setup and scenario source.<br/></param>
/// <param name="Description">Short developer-facing explanation of the type's role.<br/></param>
/// <param name="Source">Exact paste-ready declaration extracted from the same model source used by complete examples.<br/></param>
internal sealed record ScenarioTypeDefinition(string Name, string Description, string Source);

/// <summary>
/// Provides the persisted domain types used by learner-facing Abraxas examples.<br/>
/// Showcase runner, proof, browser, and transport types are intentionally excluded so the reference remains focused on application models stored by Abraxas.<br/>
/// </summary>
internal static class ScenarioTypeCatalog
{
    private static readonly string CustomerOrderSource = ExampleSource.Read("Models/CustomerOrder.cs.txt");
    private static readonly string ObjectIdentitySource = ExampleSource.Read("Models/ObjectIdentity.cs.txt");
    private static readonly string CustomerSummarySource = ExampleSource.Read("Models/CustomerSummary.cs.txt");
    private static readonly string CustomerRenamedSource = ExampleSource.Read("Models/CustomerRenamed.cs.txt");
    private static readonly string ShapeHooksSource = ExampleSource.Read("Models/ShapeHooks.cs.txt");
    private static readonly string PathGraphSource = ExampleSource.Read("Models/PathGraph.cs.txt");
    private static readonly string ObjectGraphsSource = ExampleSource.Read("Models/ObjectGraphs.cs.txt");
    private static readonly string UniqueAccountSource = ExampleSource.Read("Models/UniqueAccount.cs.txt");
    private static readonly string VaultRecordSource = ExampleSource.Read("Models/VaultRecord.cs.txt");

    internal static IReadOnlyList<ScenarioTypeDefinition> All { get; } =
    [
        Define(nameof(ShowcaseAddress), "A nested address value stored inside a customer profile.", ObjectIdentitySource),
        Define(nameof(ShowcaseAddressBook), "A List-derived address collection that also persists its own Home member.", PathGraphSource),
        Define(nameof(ShowcaseContact), "Contact details stored as one branch of a customer profile.", ObjectIdentitySource),
        Define(nameof(ShowcaseContextualPayload), "An application value whose Regex is stored as pattern and options by a versioned, context-selected type codec.", ShapeHooksSource),
        Define(nameof(ShowcaseCoordinate), "One member-bearing element of a persisted multidimensional array.", PathGraphSource),
        Define(nameof(ShowcaseCustomer), "An ordinary customer object stored without requiring an identity property.", CustomerOrderSource),
        Define(nameof(ShowcaseCustomerAlternative), "A different customer result type: matching stored paths fill its members, and one unmatched member keeps its default.", CustomerSummarySource),
        Define(nameof(ShowcaseCustomerProfile), "A root object containing nested contact, address, and preference values.", ObjectIdentitySource),
        Define(nameof(ShowcaseCustomerRenamed), "A newer customer view whose DisplayName can temporarily read the stored Name path through an alias.", CustomerRenamedSource),
        Define(nameof(ShowcaseCustomerSummary), "A smaller customer result type filled from the stored paths it shares with the full customer.", CustomerSummarySource),
        Define(nameof(ShowcaseGraphCustomer), "A customer whose related orders are found by searching the order store for its RecordID.", ObjectGraphsSource),
        Define(nameof(ShowcaseGraphLine), "A line item stored separately from its order.", ObjectGraphsSource),
        Define(nameof(ShowcaseGraphOrder), "An order that keeps line RecordIDs in order while line objects live in another store.", ObjectGraphsSource),
        Define(nameof(ShowcaseHookAddress), "A nested address created by application code before Abraxas fills its stored values.", ShapeHooksSource),
        Define(nameof(ShowcaseHookDocument), "An example object used to show values omitted from storage and custom write/read hooks.", ShapeHooksSource),
        Define(nameof(ShowcaseHookToken), "A value encoded by either a member-path or type hook.", ShapeHooksSource),
        Define(nameof(ShowcaseHookTransient), "A type the example deliberately leaves out of stored records.", ShapeHooksSource),
        Define(nameof(ShowcaseIdentityFreeNote), "A persisted note whose Abraxas RecordID remains external to the object.", ObjectIdentitySource),
        Define(nameof(ShowcaseOrder), "An order stored separately from its customer, with the customer's RecordID linking them.", CustomerOrderSource),
        Define(nameof(ShowcasePathAddress), "An address with literal string-keyed notes, reached by list position or the Home member.", PathGraphSource),
        Define(nameof(ShowcasePathGraph), "The stored root for collection, shared-reference, dictionary-key, and multidimensional PropPath examples.", PathGraphSource),
        Define(nameof(ShowcasePreferences), "Scalar and collection preferences stored inside a customer profile.", ObjectIdentitySource),
        Define(nameof(ShowcaseUniqueAccount), "An ordinary account with a number, login, and tenant used to demonstrate single-part and composite unique keys.", UniqueAccountSource),
        Define(nameof(ShowcaseVaultRecord), "A demonstration record whose payload can be compressed, encrypted, or both; SecretCode is synthetic test data.", VaultRecordSource)
    ];

    /// <summary>
    /// Reports whether exact learner-facing source contains one complete C# identifier.<br/>
    /// Boundary checks prevent a shorter type name from matching part of a longer identifier without introducing a parser dependency into the Showcase.<br/>
    /// </summary>
    /// <param name="source">Displayed setup or scenario source.<br/></param>
    /// <param name="identifier">Cataloged type identifier.<br/></param>
    /// <returns><see langword="true"/> when the identifier appears with non-identifier boundaries.<br/></returns>
    internal static bool Uses(string? source, string identifier)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(identifier)) return false;
        int start = 0;
        while ((start = source.IndexOf(identifier, start, StringComparison.Ordinal)) >= 0)
        {
            int end = start + identifier.Length;
            bool leftBoundary = start == 0 || !IsIdentifierCharacter(source[start - 1]);
            bool rightBoundary = end == source.Length || !IsIdentifierCharacter(source[end]);
            if (leftBoundary && rightBoundary) return true;
            start = end;
        }
        return false;
    }

    /// <summary>Builds one type entry from the shared paste-ready model source.<br/></summary>
    /// <param name="name">Exact public class name.<br/></param>
    /// <param name="description">Short learner-facing purpose.<br/></param>
    /// <param name="modelSource">Model source containing the declaration.<br/></param>
    /// <returns>A catalog entry retaining the exact individual declaration.<br/></returns>
    private static ScenarioTypeDefinition Define(string name, string description, string modelSource) =>
        new(name, description, ExtractDeclaration(modelSource, name));

    /// <summary>
    /// Extracts one top-level class declaration from the exact bundled model source.<br/>
    /// The model files intentionally contain property-only declarations, so balanced braces identify the complete type without changing or regenerating learner-facing code.<br/>
    /// </summary>
    /// <param name="source">Complete model source bundle.<br/></param>
    /// <param name="typeName">Exact public class name to extract.<br/></param>
    /// <returns>The unchanged declaration from <c>public sealed class</c> through its closing brace.<br/></returns>
    private static string ExtractDeclaration(string source, string typeName)
    {
        string marker = $"public sealed class {typeName}";
        int declarationStart = source.IndexOf(marker, StringComparison.Ordinal);
        if (declarationStart < 0) throw new InvalidOperationException($"The learner-facing declaration for {typeName} is missing.");
        int bodyStart = source.IndexOf('{', declarationStart);
        if (bodyStart < 0) throw new InvalidOperationException($"The learner-facing declaration for {typeName} has no body.");
        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[declarationStart..(index + 1)].Trim();
        }
        throw new InvalidOperationException($"The learner-facing declaration for {typeName} is incomplete.");
    }

    /// <summary>Reports whether one character can continue a C# identifier.<br/></summary>
    /// <param name="value">Character adjacent to a possible type-name match.<br/></param>
    /// <returns><see langword="true"/> for letters, digits, or underscore.<br/></returns>
    private static bool IsIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
