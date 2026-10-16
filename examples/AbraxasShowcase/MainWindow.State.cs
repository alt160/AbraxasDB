namespace AbraxasShowcase;

/// <summary>Projects native showcase state into the stable browser-facing view model.<br/></summary>
internal sealed partial class MainWindow
{
    /// <summary>Returns active laboratory state plus the curated scenario catalog and exact embedded method source.<br/></summary>
    /// <returns>One complete initial UI state object.<br/></returns>
    private object GetState() => new
    {
        theme = WindowsTheme.Current(),
        startup = new
        {
            requiresChoice = requiresWorkspaceChoice,
            mode = workspaceMode,
            presence = startupPresence
        },
        laboratory = workspace?.Describe(),
        commercialFeaturesAvailable = ShowcaseEdition.CommercialFeaturesAvailable,
        groupStores = workspace is null ? Array.Empty<GroupStoreDescriptor>() : workspace.DescribeActiveGroupStores(),
        groups = ScenarioGroupCatalog.All.Select(group => new
        {
            group.Id,
            group.Title,
            group.Description,
            group.SetupVersion,
            group.PrimaryCountLabel,
            group.RelatedCountLabel,
            group.DefaultPrimaryCount,
            group.DefaultRelatedCount,
            group.SetupSource,
            group.GenerationSource,
            completeSource = ExampleSource.CompleteGroup(group.Id),
            group.ReplacesEnvironmentPerScenario,
            group.CommercialOnly,
            group.Concepts,
            ready = group.ReplacesEnvironmentPerScenario || workspace?.IsGroupReady(group) == true
        }),
        types = ScenarioTypeCatalog.All.Select(type => new
        {
            type.Name,
            type.Description,
            type.Source,
            scenarioIds = ScenarioCatalog.Entries
                .Where(entry => entry.Scenario is not null && ScenarioTypeCatalog.Uses(entry.Scenario.SourceCode, type.Name))
                .Select(entry => entry.Id),
            groupIds = ScenarioGroupCatalog.All
                .Where(group => ScenarioTypeCatalog.Uses(group.SetupSource, type.Name) || ScenarioTypeCatalog.Uses(group.GenerationSource, type.Name))
                .Select(group => group.Id)
        }),
        scenarios = ScenarioCatalog.Entries
            .OrderBy(entry => entry.Documentation.LearningOrder)
            .Select(entry => new
        {
            entry.Id,
            entry.Category,
            entry.Title,
            entry.Description,
            entry.Cost,
            entry.Mutates,
            commercialOnly = entry.Scenario is not null && ScenarioGroupCatalog.ForCategory(entry.Category).CommercialOnly,
            explanation = entry.Documentation.Explanation,
            concepts = entry.Documentation.Concepts,
            apiNames = entry.Documentation.ApiNames,
            searchTerms = entry.Documentation.SearchTerms,
            prerequisites = entry.Documentation.Prerequisites,
            learningOrder = entry.Documentation.LearningOrder,
            runnable = entry.Scenario is not null,
            groupId = entry.Scenario is null ? null : ScenarioGroupCatalog.ForCategory(entry.Category).Id,
            groupReady = entry.Scenario is not null && (
                ScenarioGroupCatalog.ForCategory(entry.Category).ReplacesEnvironmentPerScenario
                || workspace?.IsGroupReady(ScenarioGroupCatalog.ForCategory(entry.Category)) == true),
            source = entry.Scenario?.SourceCode,
            completeSource = entry.Scenario?.CompleteSource
        })
    };
}
