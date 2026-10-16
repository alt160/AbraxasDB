using System.Reflection;
using System.Text.Json.Serialization;

namespace AbraxasShowcase;

/// <summary>Metadata and executable contract shared by every compiled showcase scenario.<br/></summary>
internal interface IShowcaseScenario
{
    string Id { get; }
    string Category { get; }
    string Title { get; }
    string Description { get; }
    string Cost { get; }
    bool Mutates { get; }
    ScenarioDocumentation Documentation { get; }
    string SourceCode { get; }
    string CompleteSource { get; }
    ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken);
}

/// <summary>One concise, non-object result fact shown separately from the live inspection tree.<br/></summary>
internal sealed record ScenarioFact(string Label, string Value);

/// <summary>One retained value within a scenario's consistently modeled result collection.<br/></summary>
internal sealed class ScenarioResultItem
{
    internal string Label { get; }
    internal ulong? RecordID { get; }
    [JsonIgnore] internal object? InspectionValue { get; }
    [JsonIgnore] internal Type DeclaredType { get; }

    /// <summary>Creates one result item whose actual value remains exclusively in the native host.<br/></summary>
    /// <param name="label">Short developer-facing result label.<br/></param>
    /// <param name="recordID">Optional Abraxas RecordID associated with this returned value.<br/></param>
    /// <param name="inspectionValue">Actual .NET value retained for live exploration.<br/></param>
    /// <param name="declaredType">Declared result type that governed the scenario API.<br/></param>
    internal ScenarioResultItem(string label, ulong? recordID, object? inspectionValue, Type declaredType)
    {
        Label = label;
        RecordID = recordID;
        InspectionValue = inspectionValue;
        DeclaredType = declaredType;
    }
}

/// <summary>
/// Native scenario outcome whose actual .NET value remains in C# for live, on-demand inspection.<br/>
/// JSON serialization deliberately excludes the retained object and its <see cref="Type"/> so WebView2 receives only explicit proof metadata and object-node descriptors.<br/>
/// </summary>
internal sealed class ScenarioResult
{
    internal bool Passed { get; }
    internal string Summary { get; }
    internal IReadOnlyList<ScenarioFact> Facts { get; }
    internal IReadOnlyList<ScenarioResultItem> Results { get; }
    [JsonIgnore] internal object? InspectionValue => Results[0].InspectionValue;
    [JsonIgnore] internal Type DeclaredType => Results[0].DeclaredType;
    internal double ElapsedMilliseconds { get; }

    /// <summary>Creates one result while retaining its actual inspection root exclusively in the native host.<br/></summary>
    /// <param name="passed">Whether scenario assertions succeeded.<br/></param>
    /// <param name="summary">Developer-facing assertion summary.<br/></param>
    /// <param name="facts">Scalar operation facts that are not members of the returned object.<br/></param>
    /// <param name="inspectionValue">Actual .NET value returned for later live expansion.<br/></param>
    /// <param name="declaredType">Declared result type that governed the scenario API.<br/></param>
    /// <param name="elapsedMilliseconds">Scenario duration ending before any explorer activity.<br/></param>
    internal ScenarioResult(bool passed, string summary, IReadOnlyList<ScenarioFact> facts, object? inspectionValue, Type declaredType, double elapsedMilliseconds, ulong? recordID = null, string resultLabel = "Result")
    {
        Passed = passed;
        Summary = summary;
        Facts = facts;
        Results = [new ScenarioResultItem(resultLabel, recordID, inspectionValue, declaredType)];
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>Creates one result with an explicit collection of retained values for list and comparison scenarios.<br/></summary>
    /// <param name="passed">Whether scenario assertions succeeded.<br/></param>
    /// <param name="summary">Developer-facing assertion summary.<br/></param>
    /// <param name="facts">Scalar operation facts that apply to the overall execution.<br/></param>
    /// <param name="results">One or more returned values retained for independent exploration.<br/></param>
    /// <param name="elapsedMilliseconds">Scenario duration ending before any explorer activity.<br/></param>
    internal ScenarioResult(bool passed, string summary, IReadOnlyList<ScenarioFact> facts, IReadOnlyList<ScenarioResultItem> results, double elapsedMilliseconds)
    {
        if (results.Count == 0) throw new ArgumentException("A scenario result must contain at least one returned value.", nameof(results));
        Passed = passed;
        Summary = summary;
        Facts = facts;
        Results = results;
        ElapsedMilliseconds = elapsedMilliseconds;
    }
}
