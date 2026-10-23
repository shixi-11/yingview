using System.Windows;
using System.IO;

namespace YingView;

public partial class App : Application
{
    public static string[] Arguments { get; private set; } = [];
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Arguments = e.Args;
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O}\n{args.Exception}\n");
            }
            catch { /* Logging must not interrupt the recovery dialog. */ }
            MessageBox.Show("映窗遇到问题，可重新打开图片。\nYingView encountered a problem. Please reopen the image.\n\n" + args.Exception.Message,
                "YingView", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
