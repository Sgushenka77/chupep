using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LoadLens.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LoadLens");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never hide the original failure.
        }

        MessageBox.Show(
            "LoadLens столкнулся с ошибкой:\n\n" + e.Exception.Message +
            "\n\nПодробности сохранены в %LOCALAPPDATA%\\LoadLens\\error.log",
            "LoadLens",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
