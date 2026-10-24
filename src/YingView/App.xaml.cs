using System.Windows;
using System.IO;
using System.Runtime.InteropServices;

namespace YingView;

public partial class App : Application
{
    public static string[] Arguments { get; private set; } = [];
    protected override async void OnStartup(StartupEventArgs e)
    {
        // A packaged installation keeps the identity assigned by Windows.
        var packageNameLength = 0;
        if (GetCurrentPackageFullName(ref packageNameLength, IntPtr.Zero) == 15700)
            _ = SetCurrentProcessExplicitAppUserModelID(ClientBranding.DesktopAppId(ClientBranding.InitialLanguage));
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
        var viewer = new MainWindow();
        MainWindow = viewer;
        var opening = viewer.OpenStartupAsync();
        // Give ordinary pictures a short head start so the window can appear with its image.
        // Slow files still get a responsive loading window after this bounded delay.
        if (!opening.IsCompleted) await Task.WhenAny(opening, Task.Delay(120));
        viewer.Show();
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
