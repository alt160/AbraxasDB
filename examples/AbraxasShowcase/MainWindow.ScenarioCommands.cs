using System.Diagnostics;
using System.Text.Json;

namespace AbraxasShowcase;

/// <summary>Runs registered scenarios and expands retained live C# inspection values.<br/></summary>
internal sealed partial class MainWindow
{
    /// <summary>Runs one registered compiled scenario and returns separately measured host and scenario evidence.<br/></summary>
    /// <param name="payload">Local UI JSON containing the exact registered scenario identifier.<br/></param>
    /// <returns>The scenario proof plus total host dispatch time.<br/></returns>
    private async Task<object> RunScenario(JsonElement payload)
    {
        string scenarioID = payload.GetProperty("scenarioId").GetString() ?? "";
        IShowcaseScenario scenario = ScenarioCatalog.Entries.SingleOrDefault(item => item.Id == scenarioID)?.Scenario
            ?? throw new ArgumentException("Unknown scenario.");
        ScenarioGroupDefinition group = ScenarioGroupCatalog.ForCategory(scenario.Category);
        if (group.CommercialOnly && !ShowcaseEdition.CommercialFeaturesAvailable)
            throw new InvalidOperationException("This example requires the commercial Abraxas edition. Its code and explanation remain available here.");
        if (!group.ReplacesEnvironmentPerScenario && !RequireWorkspace().IsGroupReady(group))
            throw new InvalidOperationException($"Prepare {group.Title} before running this scenario.");
        activeOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationToken token = activeOperation.Token;
        var timer = Stopwatch.StartNew();
        ScenarioResult result;
        if (group.ReplacesEnvironmentPerScenario)
        {
            workspace ??= new ShowcaseWorkspace(workspacePath, false);
            inspections.Reset();
            await Task.Run(workspace.BeginFundamentalsScenario, token);
            bool completed = false;
            try
            {
                result = await Task.Run(() => scenario.Run(workspace, token), token);
                completed = true;
            }
            finally
            {
                await Task.Run(() => workspace.CompleteFundamentalsScenario(group, scenario.Id, scenario.Title, completed), CancellationToken.None);
            }
            requiresWorkspaceChoice = false;
            workspaceMode = "active";
            startupPresence = ShowcaseWorkspace.Inspect(workspacePath);
        }
        else
        {
            result = await Task.Run(() => scenario.Run(RequireWorkspace(), token), token);
        }
        timer.Stop();
        inspections.Reset();
        object[] resultItems = result.Results
            .Select((item, index) => new
            {
                index,
                item.Label,
                recordID = item.RecordID?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                inspection = inspections.Add(item.InspectionValue, item.DeclaredType, "Result", "Scenario result")
            })
            .Cast<object>()
            .ToArray();
        return new
        {
            proof = new { result.Passed, result.Summary, result.Facts, result.ElapsedMilliseconds },
            results = resultItems,
            hostMilliseconds = timer.Elapsed.TotalMilliseconds,
            laboratory = RequireWorkspace().Describe()
        };
    }

    /// <summary>Expands one retained C# value and keeps its measured inspection cost separate from scenario execution.<br/></summary>
    /// <param name="payload">Local UI JSON containing only opaque session and node identifiers issued by C#.<br/></param>
    /// <returns>Typed child descriptors produced by the selected native viewer.<br/></returns>
    private object ExpandObject(JsonElement payload) => inspections.Expand(
        payload.GetProperty("sessionId").GetString() ?? "",
        payload.GetProperty("nodeId").GetString() ?? "");
}
