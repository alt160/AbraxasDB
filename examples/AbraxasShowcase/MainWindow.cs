using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace AbraxasShowcase;

/// <summary>Thin native host for the packaged showcase UI and allowlisted RecordBase scenario bridge.<br/></summary>
internal sealed partial class MainWindow : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly Label startup = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Opening Abraxas Showcase…" };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ObjectInspectionManager inspections = new();
    private readonly string workspacePath;
    private readonly string webViewPath;
    private readonly Icon? applicationIcon;
    private ShowcaseWorkspace? workspace;
    private LaboratoryPresence? startupPresence;
    private bool requiresWorkspaceChoice;
    private string workspaceMode = "new";
    private CancellationTokenSource? activeOperation;
    private bool closing;

    /// <summary>Creates the native window around one dedicated shared laboratory and one separate WebView2 profile.<br/></summary>
    /// <param name="workspacePath">Application-owned RecordBase laboratory path.<br/></param>
    /// <param name="webViewPath">Application-owned WebView2 profile path kept outside RecordBase regeneration.<br/></param>
    internal MainWindow(string workspacePath, string webViewPath, bool enableUiProbe = false)
    {
        this.workspacePath = Path.GetFullPath(workspacePath);
        this.webViewPath = Path.GetFullPath(webViewPath);
        Text = "Abraxas Showcase";
        applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (applicationIcon is not null) Icon = applicationIcon;
        ClientSize = new Size(1480, 920);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 15, 25);
        startup.ForeColor = Color.FromArgb(218, 229, 245);
        startup.BackColor = BackColor;
        startup.Font = new Font("Segoe UI", 13);
        Controls.Add(web);
        Controls.Add(startup);
        startup.BringToFront();
        Shown += Initialize;
        FormClosing += CloseSafely;
    }

    /// <summary>Opens the shared RecordBase off the UI thread and restricts WebView2 to packaged local content.<br/></summary>
    private async void Initialize(object? sender, EventArgs args)
    {
        try
        {
            startupPresence = await Task.Run(() => ShowcaseWorkspace.Inspect(workspacePath), lifetime.Token);
            requiresWorkspaceChoice = startupPresence.HasRecordBase;
            workspaceMode = requiresWorkspaceChoice ? "choice" : "welcome";
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewPath);
            await web.EnsureCoreWebView2Async(environment);
            ShowcaseWebAssetState assets = ShowcaseWebAssets.Resolve();
            bool packagedWeb = assets.Mode == "embedded-cache";
            string mappedFolder = packagedWeb ? Path.GetDirectoryName(assets.Root)! : assets.Root;
            string pagePath = packagedWeb ? $"/{Path.GetFileName(assets.Root)}/index.html" : "/index.html";
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("showcase.local", mappedFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.NewWindowRequested += (_, request) => request.Handled = true;
            web.CoreWebView2.PermissionRequested += (_, request) => request.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.NavigationStarting += (_, request) => request.Cancel = !request.Uri.StartsWith("https://showcase.local/", StringComparison.Ordinal);
            web.CoreWebView2.WebMessageReceived += Receive;
            web.Source = new Uri("https://showcase.local" + pagePath);
            startup.Dispose();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception error)
        {
            if (!closing) MessageBox.Show(this, error.ToString(), "Abraxas Showcase could not start");
            Close();
        }
    }

    /// <summary>Dispatches one trusted local UI request through an allowlisted command table and serialized RecordBase gate.<br/></summary>
    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (closing || !args.Source.StartsWith("https://showcase.local/", StringComparison.Ordinal)) return;
        string id = "";
        bool entered = false;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            JsonElement request = document.RootElement;
            id = request.GetProperty("id").GetString() ?? "";
            string command = request.GetProperty("command").GetString() ?? "";
            JsonElement payload = request.TryGetProperty("payload", out JsonElement value) ? value.Clone() : default;
            if (command == "cancel")
            {
                activeOperation?.Cancel();
                Reply(id, true, new { requested = activeOperation is not null });
                return;
            }
            if (command == "save-page-html")
            {
                Reply(id, true, SavePageHtml(payload));
                return;
            }

            await gate.WaitAsync(lifetime.Token);
            entered = true;
            object result = command switch
            {
                "get-state" => GetState(),
                "choose-workspace" => await ChooseWorkspace(payload),
                "clear-workspace" => await ClearWorkspace(payload),
                "prepare-group" => await PrepareGroup(payload),
                "browse-group-data" => BrowseGroupData(payload),
                "inspect-group-record" => InspectGroupRecord(payload),
                "run-scenario" => await RunScenario(payload),
                "expand-object" => ExpandObject(payload),
                _ => throw new ArgumentException("Unknown showcase command.")
            };
            Reply(id, true, result);
        }
        catch (OperationCanceledException) when (closing) { }
        catch (OperationCanceledException) { Reply(id, false, null, "Operation cancelled."); }
        catch (Exception error) { Reply(id, false, null, error.Message); }
        finally
        {
            if (entered)
            {
                activeOperation?.Dispose();
                activeOperation = null;
                gate.Release();
            }
        }
    }
    /// <summary>Posts one request-correlated success or failure response to the packaged UI.<br/></summary>
    private void Reply(string id, bool ok, object? result, string? error = null) => Post(new { id, ok, result, error });

    /// <summary>
    /// Prompts for an explicit destination and writes one self-contained static HTML snapshot supplied by the trusted local Showcase page.<br/>
    /// The dialog owns overwrite confirmation; cancelling leaves existing files unchanged.<br/>
    /// </summary>
    /// <param name="payload">Current page title and complete inline HTML document.<br/></param>
    /// <returns>Whether the user selected a destination and the page was saved.<br/></returns>
    private object SavePageHtml(JsonElement payload)
    {
        string title = payload.GetProperty("title").GetString() ?? "Abraxas Showcase";
        string html = payload.GetProperty("html").GetString() ?? "";
        if (html.Length == 0 || html.Length > 20_000_000 || !html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The page snapshot is missing or too large to save.");
        string stem = string.Join("-", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(stem)) stem = "Abraxas Showcase";
        using var dialog = new SaveFileDialog
        {
            Title = "Save Showcase page as HTML",
            FileName = stem + ".html",
            Filter = "HTML page (*.html)|*.html",
            DefaultExt = "html",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return new { saved = false };
        File.WriteAllText(dialog.FileName, html, new System.Text.UTF8Encoding(false));
        return new { saved = true };
    }

    /// <summary>Serializes one bounded event onto the UI thread when the WebView remains available.<br/></summary>
    private void Post(object message)
    {
        if (closing || web.CoreWebView2 is null) return;
        string json = JsonSerializer.Serialize(message, Wire.Options);
        if (InvokeRequired) BeginInvoke(() => { if (!closing && web.CoreWebView2 is not null) web.CoreWebView2.PostWebMessageAsJson(json); });
        else web.CoreWebView2.PostWebMessageAsJson(json);
    }

    /// <summary>Returns the initialized workspace or fails visibly if startup has not completed.<br/></summary>
    private ShowcaseWorkspace RequireWorkspace() => workspace ?? throw new InvalidOperationException("The showcase laboratory is not open.");

    /// <summary>Cancels active work, closes the RecordBase, and releases host resources without blocking on WebView2 callbacks.<br/></summary>
    private void CloseSafely(object? sender, FormClosingEventArgs args)
    {
        closing = true;
        lifetime.Cancel();
        activeOperation?.Cancel();
        inspections.Dispose();
        workspace?.Dispose();
        workspace = null;
        web.Dispose();
        applicationIcon?.Dispose();
        lifetime.Dispose();
    }
}
