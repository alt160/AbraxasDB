namespace AbraxasShowcase;

/// <summary>Ordered registry that exposes only scenarios intentionally admitted to the showcase UI.<br/></summary>
internal static class ScenarioCatalog
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        new CreateRecordBaseScenario(),
        new ReuseTypedRecordStoreScenario(),
        new CreateRecordIndexesScenario(),
        .. FeatureFoundationScenarios.All,
        .. MemoryIndexScenarios.All,
        new InspectActiveRecordBaseScenario(),
        new PutAndGetCustomerScenario(),
        new PersistNestedObjectGraphScenario(),
        new RetainExternalIdentityScenario(),
        .. OrdinaryShapeScenarios.All,
        new InspectStoreDefinitionsScenario(),
        new CountStoreRecordsScenario(),
        new ObserveStorePersistenceScenario(),
        new TraverseNaturalOrderScenario(),
        .. AdditionalScenarios.All,
        .. PathNavigationScenarios.All,
        .. MeridianSearchScenarios.All,
        .. ShapeGraphScenarios.All,
        .. GroupDepthScenarios.All,
        .. AnalysisScenarios.All,
        .. CommercialPayloadScenarios.All
    ];

    internal static IReadOnlyList<ScenarioCatalogEntry> Entries { get; } =
    [
        .. All.Select(ScenarioCatalogEntry.Runnable),
        .. ScenarioRoadmap.All.Where(planned => All.All(runnable => runnable.Id != planned.Id))
    ];
}

/// <summary>Structured explanatory metadata shared by the catalog, search index, and executable scenario workspace.<br/></summary>
/// <param name="Explanation">Longer explanation of what the developer will learn and observe.<br/></param>
/// <param name="Concepts">Conceptual topics taught by the scenario.<br/></param>
/// <param name="ApiNames">Abraxas API or type names that should match reference searches.<br/></param>
/// <param name="SearchTerms">Additional developer vocabulary that should find the scenario.<br/></param>
/// <param name="Prerequisites">Earlier examples that prepare the developer for this scenario.<br/></param>
/// <param name="LearningOrder">Stable ordering within the complete learning path.<br/></param>
internal sealed record ScenarioDocumentation(
    string Explanation,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<string> ApiNames,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<string> Prerequisites,
    int LearningOrder);

/// <summary>One normalized catalog entry, optionally backed by a compiled runnable scenario.<br/></summary>
internal sealed record ScenarioCatalogEntry(
    string Id,
    string Category,
    string Title,
    string Description,
    string Cost,
    bool Mutates,
    ScenarioDocumentation Documentation,
    IShowcaseScenario? Scenario)
{
    /// <summary>Creates a catalog entry from the same compiled scenario that supplies execution and exact source.<br/></summary>
    /// <param name="scenario">Registered executable scenario implementation.<br/></param>
    /// <returns>A runnable catalog entry with no duplicated scenario metadata.<br/></returns>
    internal static ScenarioCatalogEntry Runnable(IShowcaseScenario scenario) => new(
        scenario.Id,
        scenario.Category,
        scenario.Title,
        scenario.Description,
        scenario.Cost,
        scenario.Mutates,
        scenario.Documentation,
        scenario);

    /// <summary>Creates a clearly non-runnable roadmap entry with the same documentation shape as compiled examples.<br/></summary>
    /// <param name="id">Stable future scenario identifier.<br/></param>
    /// <param name="category">Developer-facing category name.<br/></param>
    /// <param name="title">Concise scenario title.<br/></param>
    /// <param name="description">One-sentence reference summary.<br/></param>
    /// <param name="explanation">Longer learning and behavior explanation.<br/></param>
    /// <param name="cost">Expected qualitative execution-cost guidance.<br/></param>
    /// <param name="mutates">Whether the eventual runner is expected to mutate the shared laboratory.<br/></param>
    /// <param name="concepts">Conceptual topics taught by the scenario.<br/></param>
    /// <param name="apiNames">Expected public API or type search terms.<br/></param>
    /// <param name="searchTerms">Additional developer vocabulary.<br/></param>
    /// <param name="prerequisites">Earlier examples that prepare the developer for this one.<br/></param>
    /// <param name="learningOrder">Stable ordering within the complete learning path.<br/></param>
    /// <returns>A planned entry that cannot be executed or expose claimed source.<br/></returns>
    internal static ScenarioCatalogEntry Planned(
        string id,
        string category,
        string title,
        string description,
        string explanation,
        string cost,
        bool mutates,
        IReadOnlyList<string> concepts,
        IReadOnlyList<string> apiNames,
        IReadOnlyList<string> searchTerms,
        IReadOnlyList<string> prerequisites,
        int learningOrder) => new(
            id,
            category,
            title,
            description,
            cost,
            mutates,
            new ScenarioDocumentation(explanation, concepts, apiNames, searchTerms, prerequisites, learningOrder),
            null);
}
