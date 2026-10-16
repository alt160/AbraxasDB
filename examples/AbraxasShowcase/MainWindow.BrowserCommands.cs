using System.Text.Json;

namespace AbraxasShowcase;

/// <summary>Handles type-agnostic group-data browsing and selected-record inspection commands.<br/></summary>
internal sealed partial class MainWindow
{
    /// <summary>Returns one explicit page of dynamic values for an active matching scenario-group store.<br/></summary>
    /// <param name="payload">Local UI JSON containing group, store, properties, page, and page-size selections.<br/></param>
    /// <returns>A page of identity-aware values read without hydrating complete objects.<br/></returns>
    private object BrowseGroupData(JsonElement payload)
    {
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(payload.GetProperty("groupId").GetString() ?? "");
        ShowcaseWorkspace current = RequireWorkspace();
        if (!current.IsGroupReady(group)) throw new InvalidOperationException($"Prepare {group.Title} before browsing its data.");
        string storeName = payload.GetProperty("storeName").GetString() ?? "";
        string[] propertyPaths = payload.GetProperty("propertyPaths").EnumerateArray()
            .Select(item => item.GetString() ?? "")
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        int pageIndex = payload.TryGetProperty("pageIndex", out JsonElement page) ? page.GetInt32() : 0;
        int pageSize = payload.TryGetProperty("pageSize", out JsonElement size) ? size.GetInt32() : 25;
        inspections.Reset();
        return current.BrowseActiveStore(storeName, propertyPaths, pageIndex, pageSize);
    }

    /// <summary>Hydrates exactly one selected browser row and begins a truthful live-object inspection session for it.<br/></summary>
    /// <param name="payload">Local UI JSON containing the active group, store name, and external Abraxas identity.<br/></param>
    /// <returns>A root descriptor retaining the real C# object until the browser page changes.<br/></returns>
    private object InspectGroupRecord(JsonElement payload)
    {
        ScenarioGroupDefinition group = ScenarioGroupCatalog.Get(payload.GetProperty("groupId").GetString() ?? "");
        ShowcaseWorkspace current = RequireWorkspace();
        if (!current.IsGroupReady(group)) throw new InvalidOperationException($"Prepare {group.Title} before browsing its data.");
        string storeName = payload.GetProperty("storeName").GetString() ?? "";
        ulong recordID = ulong.Parse(payload.GetProperty("recordId").GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture);
        (object value, Type declaredType) = current.GetActiveGroupRecord(storeName, recordID);
        return inspections.Add(value, declaredType, $"Record {recordID}", $"{storeName}.Get({recordID})");
    }
}
