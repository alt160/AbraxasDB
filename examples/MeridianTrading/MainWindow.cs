using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace MeridianTrading;

/// <summary>Thin desktop host with a local-origin, allowlisted asynchronous request bridge.<br/></summary>
internal sealed class MainWindow : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly string dataPath;
    private TradingDatabase? database;
    private bool closing;
    private CancellationTokenSource? generationCancellation;
    private readonly RequestFreshness readRequests = new();
    private readonly RuntimeMetricsSampler runtimeMetrics = new();
    private readonly ProgressBar startupProgress = new() { Dock = DockStyle.Bottom, Height = 5, Style = ProgressBarStyle.Marquee };
    private readonly Label startup = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Opening Meridian workspace…", ForeColor = Color.FromArgb(35, 70, 60), Font = new Font("Segoe UI", 13) };

    /// <summary>Creates the native window without opening the dataset on the UI thread.<br/></summary>
    internal MainWindow(string dataPath, string? capturePath = null, bool captureCancel = false)
    {
        this.dataPath = dataPath;
        Text = "Meridian Trading · Abraxas";
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 246, 242);
        Controls.Add(web);
        Controls.Add(startup);
        startup.BringToFront();
        Controls.Add(startupProgress); startupProgress.BringToFront();
        Shown += Initialize;
        FormClosing += CloseSafely;
    }

    /// <summary>Opens the database off-thread and restricts navigation and messages to packaged application content.<br/></summary>
    private async void Initialize(object? sender, EventArgs args)
    {
        bool entered = false;
        try
        {
            await gate.WaitAsync(lifetime.Token);
            entered = true;
            database = await Task.Run(() =>
            {
                var opened = new TradingDatabase(dataPath);
                try
                {
                    if (opened.RecoveryMigrationRequired)
                    {
                        if (!closing) BeginInvoke(() => startup.Text = "Legacy workspace opened · migration is available on Runtime metrics");
                    }
                    else
                    {
                        opened.PrepareApplicationIndexes(message => { if (!closing) BeginInvoke(() => startup.Text = message); });
                    }
                    return opened;
                }
                catch { opened.Dispose(); throw; }
            });
            if (closing) { database.Dispose(); database = null; return; }
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(dataPath, "webview"));
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("meridian.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith("https://meridian.local/", StringComparison.Ordinal);
            web.CoreWebView2.WebMessageReceived += Receive;
            web.Source = new Uri("https://meridian.local/index.html");
            startup.Dispose();
            startupProgress.Visible = false;
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error)
        {
            if (!closing) MessageBox.Show(this, error.ToString(), "Meridian could not start");
            Close();
        }
        finally { if (entered) gate.Release(); }
    }

    /// <summary>Dispatches only known commands under one application gate and returns measured response metadata.<br/></summary>
    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.Source.StartsWith("https://meridian.local/", StringComparison.Ordinal) || closing) return;
        string id = "";
        bool entered = false;
        bool generating = false;
        RequestFreshness.Ticket? readTicket = null;
        ActionTiming? actionTiming = null;
        double queueMs = 0;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var request = document.RootElement;
            id = request.GetProperty("id").GetString() ?? "";
            string command = request.GetProperty("command").GetString() ?? "";
            var payload = request.GetProperty("payload").Clone();
            long view = request.TryGetProperty("view", out var viewValue) ? viewValue.GetInt64() : 0;
            readRequests.AdvanceView(view);
            readTicket = readRequests.Observe(command, view);
            if (command == "invalidate-reads")
            {
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, result = new { invalidated = true } }, Wire.Options));
                return;
            }
            if (command == "request-stats")
            {
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, result = new { skipped = readRequests.Skipped } }, Wire.Options));
                return;
            }
            if (command == "runtime-metrics")
            {
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, result = runtimeMetrics.Sample() }, Wire.Options));
                return;
            }
            if (command == "cancel-generation")
            {
                generationCancellation?.Cancel();
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, result = new { requested = generationCancellation is not null } }, Wire.Options));
                return;
            }
            if (command == "new-workspace")
            {
                string fresh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeridianTrading", "datasets", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add("--data"); start.ArgumentList.Add(fresh);
                Process.Start(start);
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, result = new { directory = fresh } }, Wire.Options));
                return;
            }
            PostPhase(id, "Queued — waiting for the current operation");
            var queued = Stopwatch.StartNew();
            await gate.WaitAsync(lifetime.Token);
            queueMs = queued.Elapsed.TotalMilliseconds;
            entered = true;
            if (!readRequests.IsCurrent(readTicket))
            {
                readRequests.RecordSkipped();
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = false, superseded = true, error = "Request superseded." }, Wire.Options));
                return;
            }
            if (command == "generate-profile")
            {
                generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                generating = true;
            }
            var timer = Stopwatch.StartNew();
            object? result = await Task.Run(() =>
            {
                using (actionTiming = new ActionTiming(phase => PostPhase(id, phase)))
                {
                    ActionTiming.Phase("Running " + command);
                    return Dispatch(id, command, payload);
                }
            }, lifetime.Token);
            double executionMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var resultJson = JsonSerializer.SerializeToElement(result, Wire.Options);
            double serializationMs = timer.Elapsed.TotalMilliseconds;
            var timing = new { databaseMs = actionTiming.DatabaseMs, calculationMs = actionTiming.CalculationMs, queueMs, serializationMs };
            int? returnedRows = resultJson.ValueKind == JsonValueKind.Array ? resultJson.GetArrayLength() :
                resultJson.ValueKind == JsonValueKind.Object && resultJson.TryGetProperty("rows", out var rows) ? rows.GetArrayLength() : null;
            string body = JsonSerializer.Serialize(new { id, ok = true, result = resultJson, executionMs, timing, returnedRows }, Wire.Options);
            if (!closing)
            {
                web.CoreWebView2.PostWebMessageAsJson(readRequests.IsCurrent(readTicket) ? body : JsonSerializer.Serialize(new { id, ok = false, superseded = true, error = "Request superseded." }, Wire.Options));
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "diagnostic", id, command, executionMs, serializationMs, timing, returnedRows, bytes = System.Text.Encoding.UTF8.GetByteCount(body) }, Wire.Options));
            }
        }
        catch (Exception error)
        {
            if (!closing && web.CoreWebView2 is not null)
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = false, superseded = !readRequests.IsCurrent(readTicket), error = readRequests.IsCurrent(readTicket) ? error.Message : "Request superseded.", timing = new { databaseMs = actionTiming?.DatabaseMs ?? 0, calculationMs = actionTiming?.CalculationMs ?? 0, queueMs, serializationMs = 0 } }, Wire.Options));
        }
        finally { if (generating) { generationCancellation?.Dispose(); generationCancellation = null; } if (entered) gate.Release(); }
    }

    /// <summary>Maps each wire command to a concrete application operation; no caller-supplied code is evaluated.<br/></summary>
    /// <param name="id">Request identity used to correlate incremental progress with the initiating browser action.<br/></param>
    /// <param name="command">Allow-listed application command.<br/></param>
    /// <param name="payload">Detached JSON payload for the command.<br/></param>
    /// <returns>The command result to serialize back to WebView2.<br/></returns>
    private object? Dispatch(string id, string command, JsonElement payload)
    {
        var db = database ?? throw new InvalidOperationException("Dataset is opening.");
        switch (command)
        {
            case "overview": return db.Overview();
            case "dataset": return db.DatasetInfo();
            case "storage": return db.StorageInfo();
            case "readiness": return new { manifest = db.Manifest(), indexCatalogFileFormatVersion = db.IndexCatalogFileFormatVersion, recoveryMigrationRequired = db.RecoveryMigrationRequired };
            case "audit-dataset":
                return db.AuditDataset(state => PostDatasetAuditProgress(id, state), lifetime.Token);
            case "migrate-recovery-format":
                return db.MigrateRecoveryFormat(ActionTiming.Phase, lifetime.Token);
            case "generate-profile":
                db.GenerateProfile(GenerationProfile.Resolve(payload.GetProperty("profile").GetString()!, payload.TryGetProperty("orders", out var requested) ? requested.GetInt32() : 0), state =>
                {
                    if (!closing) BeginInvoke(() => web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "generation-progress", state }, Wire.Options)));
                }, generationCancellation!.Token);
                return new { ready = true };
            case "page": return db.Page(payload.GetProperty("entity").GetString()!, payload.GetProperty("offset").GetInt32(), payload.GetProperty("query").GetString()!, payload.TryGetProperty("sort", out var sort) ? sort.GetString()! : "", payload.TryGetProperty("descending", out var descending) && descending.GetBoolean(), payload.TryGetProperty("searchField", out var searchField) ? searchField.GetString()! : "", payload.TryGetProperty("searchMode", out var searchMode) ? searchMode.GetString()! : "contains", !payload.TryGetProperty("ignoreCase", out var matchCase) || matchCase.GetBoolean());
            case "search-schema": return db.SearchSchema();
            case "describe-search": return db.DescribeSearch(payload);
            case "search-records": return db.SearchRecords(payload);
            case "lookup-record": return db.LookupRecord(payload.GetProperty("entity").GetString()!, ulong.Parse(payload.GetProperty("id").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            case "edit-detail": return db.EditDetail(payload.GetProperty("entity").GetString()!, ulong.Parse(payload.GetProperty("id").GetString()!));
            case "customer-hydration-options": return db.CustomerHydrationOptions();
            case "customer-graph": return db.CustomerObjectGraph(ulong.Parse(payload.GetProperty("id").GetString()!), payload.GetProperty("hydration"));
            case "customer-orders": return db.CustomerOrders(ulong.Parse(payload.GetProperty("id").GetString()!), payload.GetProperty("offset").GetInt32());
            case "group-orders": return db.GroupOrders(payload.GetProperty("group").GetString()!, payload.GetProperty("key").GetString()!, payload.GetProperty("offset").GetInt32());
            case "save-edit": db.SaveEdit(payload.GetProperty("entity").GetString()!, ulong.Parse(payload.GetProperty("id").GetString()!), payload.GetProperty("revision").GetString()!, payload.GetProperty("fields")); return new { saved = true };
            case "detail": return db.OrderDetail(ulong.Parse(payload.GetProperty("id").GetString()!));
            case "status": db.SaveEdit("orders", ulong.Parse(payload.GetProperty("id").GetString()!), payload.GetProperty("revision").GetString()!, payload); return new { saved = true };
            case "analyze": return db.Analyze(payload.GetProperty("group").GetString()!);
            case "generate":
                db.Generate(payload.GetProperty("orders").GetInt32(), count =>
                {
                    if (!closing) BeginInvoke(() => web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "progress", count }, Wire.Options)));
                }, lifetime.Token);
                return db.Overview();
            default: throw new ArgumentException("Unknown application command.");
        }
    }

    /// <summary>Cancels pending work and waits for the active command before closing the owned database.<br/></summary>
    private async void CloseSafely(object? sender, FormClosingEventArgs args)
    {
        if (closing) return;
        args.Cancel = true;
        closing = true;
        startupProgress.Visible = true; startupProgress.BringToFront();
        if (web.CoreWebView2 is not null)
        {
            try { await web.CoreWebView2.ExecuteScriptAsync("MeridianActivity.closing()"); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        if (!startup.IsDisposed) startup.Text = "Finishing the current storage operation before closing…";
        lifetime.Cancel();
        await gate.WaitAsync();
        try { database?.Dispose(); }
        finally { gate.Release(); }
        Close();
    }

    /// <summary>Posts request-correlated phase changes to WebView without touching controls from the worker thread.<br/>
    /// Phase messages carry no fabricated progress percentages and are ignored by obsolete browser actions.<br/></summary>
    private void PostPhase(string id, string phase)
    {
        if (closing || IsDisposed) return;
        BeginInvoke(() => { if (!closing && web.CoreWebView2 is not null) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "operation-phase", id, phase }, Wire.Options)); });
    }

    /// <summary>
    /// Posts one request-correlated Dataset audit observation from the storage worker to WebView2.<br/>
    /// The payload contains measured completed counts and explicitly marks snapshot-preparation phases as indeterminate; obsolete requests are ignored by the browser's pending-request map.<br/>
    /// </summary>
    /// <param name="id">Wire request whose action owns this progress update.<br/></param>
    /// <param name="state">Real native audit counts and phase state.<br/></param>
    private void PostDatasetAuditProgress(string id, DatasetAuditProgress state)
    {
        if (closing || IsDisposed) return;
        BeginInvoke(() =>
        {
            if (!closing && web.CoreWebView2 is not null)
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "dataset-audit-progress", id, state }, Wire.Options));
        });
    }
}
