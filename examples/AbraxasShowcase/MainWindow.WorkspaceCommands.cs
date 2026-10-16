using System.Text.Json;

namespace AbraxasShowcase;

/// <summary>Handles explicit RecordBase selection and scenario-group preparation commands.<br/></summary>
internal sealed partial class MainWindow
{
    /// <summary>
    /// Applies the explicit startup decision to open a previously generated owned RecordBase.<br/>
    /// Destructive replacement is intentionally handled only by the separately confirmed creation-lightbox command.<br/>
    /// </summary>
    /// <param name="payload">Local UI JSON containing the <c>open</c> action.<br/></param>
    /// <returns>Fresh complete UI state after the decision has been applied.<br/></returns>
    private async Task<object> ChooseWorkspace(JsonElement payload)
    {
        if (!requiresWorkspaceChoice) throw new InvalidOperationException("The startup workspace decision has already been made.");
        string action = payload.GetProperty("action").GetString() ?? "";
        if (action != "open") throw new ArgumentException("Choose open.");
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(payload.GetProperty("groupId").GetString() ?? "");
        ShowcaseWorkspace candidate = await Task.Run(() => new ShowcaseWorkspace(workspacePath), lifetime.Token);
        if (!candidate.IsGroupReady(group))
        {
            candidate.Dispose();
            throw new InvalidOperationException($"The saved Showcase RecordBase does not contain a ready {group.Title} environment.");
        }
        workspace = candidate;
        requiresWorkspaceChoice = false;
        workspaceMode = "existing";
        startupPresence = ShowcaseWorkspace.Inspect(workspacePath);
        return GetState();
    }

    /// <summary>Parses one bounded generation request shared by initial creation and later regeneration routes.<br/></summary>
    /// <param name="payload">Local UI JSON containing the explicit laboratory controls.<br/></param>
    /// <returns>A typed request that receives domain validation inside <see cref="ShowcaseWorkspace"/>.<br/></returns>
    private static GenerationRequest ParseGenerationRequest(JsonElement payload) => new(
        payload.GetProperty("size").GetString() ?? "Custom",
        payload.GetProperty("complexity").GetString() ?? "Simple",
        payload.GetProperty("customerCount").GetInt32(),
        payload.GetProperty("ordersPerCustomer").GetInt32(),
        payload.GetProperty("seed").GetInt32(),
        payload.TryGetProperty("persistAcrossRuns", out JsonElement persist) && persist.GetBoolean());

    /// <summary>
    /// Destructively replaces the one active environment with a complete registered scenario-group setup.<br/>
    /// The group setup and generation methods execute in their displayed order against an empty owned directory.<br/>
    /// Commercial-only setup is rejected in OSS before opening, closing, or replacing any workspace.<br/>
    /// </summary>
    /// <param name="payload">Confirmed group identifier plus generation parameters from the group page.<br/></param>
    /// <returns>Fresh complete UI state after the new group environment is ready.<br/></returns>
    private async Task<object> PrepareGroup(JsonElement payload)
    {
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(payload.GetProperty("groupId").GetString() ?? "");
        if (group.CommercialOnly && !ShowcaseEdition.CommercialFeaturesAvailable)
            throw new InvalidOperationException("This example requires the commercial Abraxas library. Its code and explanation remain available here; no workspace was changed.");
        if (workspace is not null && !workspace.IsGroupActive(group))
            throw new InvalidOperationException($"Unload and delete the open {workspace.ActiveGroupTitle} RecordBase before preparing {group.Title}.");
        GenerationRequest request = ParseGenerationRequest(payload);
        activeOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationToken token = activeOperation.Token;
        inspections.Reset();
        workspace ??= await Task.Run(() => new ShowcaseWorkspace(workspacePath), token);
        try
        {
            await Task.Run(() => workspace.PrepareGroup(group, request, (completed, total, phase) =>
                Post(new { kind = "generation-progress", completed, total, phase }), token), token);
        }
        catch
        {
            workspace.Dispose();
            workspace = null;
            requiresWorkspaceChoice = false;
            workspaceMode = "welcome";
            startupPresence = ShowcaseWorkspace.Inspect(workspacePath);
            throw;
        }

        requiresWorkspaceChoice = false;
        workspaceMode = "active";
        startupPresence = ShowcaseWorkspace.Inspect(workspacePath);
        return GetState();
    }

    /// <summary>
    /// Closes and permanently removes the explicitly identified active Showcase group environment.<br/>
    /// Retained inspection objects are released before the RecordBase closes, and only the constructor-normalized Showcase-owned directory can be deleted.<br/>
    /// </summary>
    /// <param name="payload">Local UI JSON containing the active group identifier confirmed in the cleanup lightbox.<br/></param>
    /// <returns>Fresh complete UI state with no open or saved Showcase RecordBase.<br/></returns>
    private async Task<object> ClearWorkspace(JsonElement payload)
    {
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(payload.GetProperty("groupId").GetString() ?? "");
        ShowcaseWorkspace current = RequireWorkspace();
        if (!current.IsGroupActive(group))
            throw new InvalidOperationException($"{group.Title} is not the active Showcase group.");

        inspections.Reset();
        try
        {
            await Task.Run(current.DeleteEnvironment, lifetime.Token);
        }
        finally
        {
            current.Dispose();
            workspace = null;
            requiresWorkspaceChoice = false;
            workspaceMode = "welcome";
            startupPresence = ShowcaseWorkspace.Inspect(workspacePath);
        }

        return GetState();
    }
}
