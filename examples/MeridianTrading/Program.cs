using System.Text.Json;

namespace MeridianTrading;

internal static class Program
{
    /// <summary>Starts the public reference app with exclusive ownership of one dataset directory.<br/>
    /// Private benchmark, capture and verification routes are not included in this application.<br/></summary>
    /// <param name="args">Optional --data directory, resolved against the process working directory when relative.<br/></param>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int index = Array.IndexOf(args, "--data");
        string path = index >= 0 && index + 1 < args.Length ? args[index + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeridianTrading", "smoke-v1");
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var datasetMutex = new Mutex(false, "Local\\MeridianTrading-" + key);
        bool acquired;
        try { acquired = datasetMutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            MessageBox.Show("This Meridian dataset is already open in another app instance.", "Dataset in use");
            return;
        }
        try { Application.Run(new MainWindow(path)); }
        finally { datasetMutex.ReleaseMutex(); }
    }
}

/// <summary>Browser wire contract; UInt64 values remain exact decimal strings.<br/></summary>
internal static class Wire
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new IdentityConverter() } };
    private sealed class IdentityConverter : System.Text.Json.Serialization.JsonConverter<ulong>
    {
        /// <summary>Reads exact string identities or integer tokens.<br/></summary>
        public override ulong Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String ? ulong.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : reader.GetUInt64();
        /// <summary>Writes identities without JavaScript precision loss.<br/></summary>
        public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
