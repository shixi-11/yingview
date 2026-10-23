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
            bool zh = Current.MainWindow is MainWindow viewer ? viewer.IsChinese : ClientBranding.InitialLanguage == "zh";
            MessageBox.Show(zh ? "映窗遇到问题，请重新打开图片。" : "YingView encountered a problem. Please reopen the image.",
                zh ? "映窗" : "YingView", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
