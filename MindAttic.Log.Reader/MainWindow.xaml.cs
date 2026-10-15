using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
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
                    await PostAsync("onApps", queryService.DiscoverApps());
                    await PostAsync("onSettings", settingsStore.Load());
                    break;

                case "selectApp":
                    await HandleSelectAppAsync(msg!);
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

    private Task HandleSelectAppAsync(JsonNode msg)
    {
        var appId = msg["appId"]!.GetValue<string>();
        var settings = settingsStore.Load();
        settings.LastAppId = appId;
        settingsStore.Save(settings);
        return Task.CompletedTask;
    }

    private async Task HandleQueryAsync(JsonNode msg)
    {
        var sourceNode = msg["source"] ?? throw new InvalidOperationException("Missing 'source'.");
        var source = new LogSource
        {
            AppId = sourceNode["appId"]!.GetValue<string>(),
            Path = sourceNode["path"]!.GetValue<string>(),
        };
        // BUG FIX: JsonNode.Deserialize<T>() throws on a null receiver rather than returning null,
        // so the "?? new LogFilter()" fallback below never actually ran for a missing "filter" key
        // — it looked defensive but wasn't. Caught by the outer try/catch either way (surfaced as
        // onError), so not a crash, just a filter-less query silently failing instead of defaulting.
        var filter = msg["filter"] is { } filterNode
            ? filterNode.Deserialize<LogFilter>(JsonOptions) ?? new LogFilter()
            : new LogFilter();

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
