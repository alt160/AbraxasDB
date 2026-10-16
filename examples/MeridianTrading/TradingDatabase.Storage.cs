namespace MeridianTrading;

internal sealed partial class TradingDatabase
{
    /// <summary>Reads the logical lengths of this application's RecordBase files without opening their contents.<br/>
    /// Includes identity and coordination sidecars, excludes browser caches, and preserves unknown related files in the total.<br/>
    /// The host gate prevents concurrent application writes; this is not allocated-cluster usage or a cross-process snapshot.<br/></summary>
    internal object StorageInfo()
    {
        using var work = ActionTiming.Calculate();
        var files = new DirectoryInfo(DirectoryPath).EnumerateFiles("meridian.*", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new { name = file.Name, bytes = file.Length, component = StorageComponent(file.Name) }).ToArray();
        return new { measuredAt = DateTimeOffset.Now, totalBytes = files.Sum(file => file.bytes), files,
            components = files.GroupBy(file => file.component).Select(group => new { name = group.Key, bytes = group.Sum(file => file.bytes), count = group.Count() }).ToArray(),
            note = "Logical file lengths, including identity and shared-memory sidecars. Excludes WebView cache and backups. Inheto payloads and Abraxas metadata are embedded in these files, not additional files." };
    }

    /// <summary>Labels the known Meridian RecordBase file contract without silently omitting future sidecars.<br/></summary>
    private static string StorageComponent(string name) => name.ToLowerInvariant() switch
    {
        "meridian.fractd" => "Fractal · record data",
        "meridian.fractx" => "Fractal · identity index",
        "meridian.lbdx" => "LibraDex · index catalog",
        "meridian.idgen" => "Abraxas · identity generator",
        "meridian.fractd.shm" or "meridian.fractx.shm" => "Fractal · shared-memory sidecars",
        _ => "Other RecordBase files"
    };
}
