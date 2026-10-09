using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MindAttic.Log.Reader.Services;

namespace MindAttic.Log.Reader;

/// <summary>
/// Single WebView2 pane, vanilla JS in wwwroot — same hosting shape as Automata.App and
/// KdpPublish (see those apps' MainWindow for the pattern this follows). The viewer never writes
/// to a log store; every bridge message it handles is a read.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LogQueryService queryService;
    private readonly ReaderSettingsStore settingsStore;

    public MainWindow()
    {
        InitializeComponent();
        queryService = App.Services.GetRequiredService<LogQueryService>();
        settingsStore = App.Services.GetRequiredService<ReaderSettingsStore>();
        _ = InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MindAttic", "MindAttic.Log.Reader", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await Browser.EnsureCoreWebView2Async(env);

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "mindattic-log-reader.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        Browser.CoreWebView2.WebMessageReceived += OnMessage;

#if DEBUG
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F11) Browser.CoreWebView2?.OpenDevToolsWindow(); };
#endif

        Browser.CoreWebView2.Navigate("https://mindattic-log-reader.local/index.html");
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(args.WebMessageAsJson); }
        catch { return; }
        var action = msg?["action"]?.GetValue<string>();

        try
        {
            switch (action)
            {
                case "ready":
                    await PostAsync("onSettings", settingsStore.Load());
                    break;

                case "browseFolder":
                    await HandleBrowseFolderAsync();
                    break;

                case "browseFile":
                    await HandleBrowseFileAsync();
                    break;

                case "query":
                    await HandleQueryAsync(msg!);
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            await PostAsync("onError", new { message = ex.Message });
        }
    }

    private async Task HandleBrowseFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder of MindAttic.Log.*.db files" };
        if (dialog.ShowDialog(this) != true) return;

        var settings = settingsStore.Load();
        settings.LastFolderPath = dialog.FolderName;
        settings.LastSourceKind = nameof(LogSourceKind.Folder);
        settingsStore.Save(settings);

        await PostAsync("onSourceChosen", new { kind = "folder", path = dialog.FolderName });
    }

    private async Task HandleBrowseFileAsync()
    {
        var dialog = new OpenFileDialog { Title = "Choose a MindAttic.Log SQLite database", Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;

        var settings = settingsStore.Load();
        settings.LastSqliteFilePath = dialog.FileName;
        settings.LastSourceKind = nameof(LogSourceKind.SqliteFile);
        settingsStore.Save(settings);

        await PostAsync("onSourceChosen", new { kind = "sqliteFile", path = dialog.FileName });
    }

    private async Task HandleQueryAsync(JsonNode msg)
    {
        var sourceNode = msg["source"] ?? throw new InvalidOperationException("Missing 'source'.");
        var kind = sourceNode["kind"]!.GetValue<string>() switch
        {
            "folder" => LogSourceKind.Folder,
            "sqliteFile" => LogSourceKind.SqliteFile,
            "sqlServer" => LogSourceKind.SqlServer,
            var other => throw new InvalidOperationException($"Unknown source kind '{other}'."),
        };
        var source = new LogSource { Kind = kind, Path = sourceNode["path"]!.GetValue<string>() };
        var filter = msg["filter"].Deserialize<LogFilter>(JsonOptions) ?? new LogFilter();

        var results = await queryService.QueryAsync(source, filter);
        await PostAsync("onResults", results);
    }

    private async Task PostAsync(string handler, object payload)
    {
        if (Browser.CoreWebView2 == null) return;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await Browser.CoreWebView2.ExecuteScriptAsync($"window.mindAtticLogReader.{handler}({json})");
    }
}
