using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MindAttic.Log.Reader.Services;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;

namespace MindAttic.Log.Reader;

/// <summary>One DI registration point — mirrors Automata.App's App.xaml.cs shape
/// (Host.CreateDefaultBuilder + one ConfigureServices call).</summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mindattic-log-reader-error.log");
        DispatcherUnhandledException += (_, ex) =>
        {
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] DISPATCHER: {ex.Exception}\n\n");
            MessageBox.Show(
                $"MindAttic.Log.Reader hit an unexpected error and logged details to:\n{logPath}\n\nYou can keep working.",
                "MindAttic.Log.Reader", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] APPDOMAIN: {ex.ExceptionObject}\n\n");

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) => cfg.AddMindAtticVaultFiles())
            .ConfigureServices((ctx, services) => services
                .AddMindAtticVault(ctx.Configuration)
                .AddVaultAppSettings<ReaderSettings>("MindAttic.Log.Reader")
                .AddSingleton<ReaderSettingsStore>()
                .AddSingleton<LogQueryService>())
            .Build();

        Services = host.Services;
        new MainWindow().Show();
    }
}
