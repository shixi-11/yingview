using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace YingView.Updates;

/// <summary>Non-blocking update UI. Installation is intentionally delegated to the host application.</summary>
public sealed class UpdateWindow : Window
{
    private readonly UpdateService _service;
    private readonly Version _current;
    private readonly string _language;
    private readonly bool _storeManaged;
    private readonly TextBlock _status;
    private readonly TextBlock _details;
    private readonly ProgressBar _downloadProgress;
    private readonly Button _releaseNotes;
    private readonly Button _primary;
    private readonly Button _cancel;
    private CancellationTokenSource? _operation;
    private UpdateCheckResult? _available;
    private VerifiedUpdateDownload? _download;
    private bool _busy;

    public event Action<object?, VerifiedUpdateDownload>? InstallAndRestartRequested;

    public UpdateWindow(Version? currentVersion = null, string? language = null, bool storeManaged = false, UpdateService? service = null, UpdateCheckResult? initialResult = null)
    {
        _service = service ?? new UpdateService();
        _current = currentVersion ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        _language = language ?? ClientBranding.InitialLanguage;
        _storeManaged = storeManaged;
        Title = T("映窗更新", "YingView updates");
        Width = 500; Height = 360; MinWidth = 440; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ShellBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontSizeProperty, "ControlTextSize");
        var root = new DockPanel { Margin = new Thickness(24) };
        TextElement.SetForeground(root, Foreground);
        TextElement.SetFontSize(root, FontSize);
        Content = root;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        _cancel = new Button { Content = T("关闭", "Close"), IsCancel = true, MinWidth = 82, Margin = new Thickness(0, 0, 8, 0) };
        _cancel.Click += (_, _) => { if (_busy) _operation?.Cancel(); else Close(); };
        actions.Children.Add(_cancel);
        _primary = new Button { Content = T("检查更新", "Check for updates"), MinWidth = 132, Padding = new Thickness(16, 8, 16, 8) };
        _primary.SetResourceReference(BackgroundProperty, "AccentBrush");
        _primary.SetResourceReference(ForegroundProperty, "AccentTextBrush");
        _primary.Click += async (_, _) => await PrimaryActionAsync();
        actions.Children.Add(_primary);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = T("软件更新", "Software updates"), FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBlock { Text = T("当前版本", "Current version"), Margin = new Thickness(0, 0, 0, 3), FontSize = 11 });
        body.Children.Add(new TextBlock { Text = $"v{_current.ToString(3)}", Margin = new Thickness(0, 0, 0, 14), FontWeight = FontWeights.Medium });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 8) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); body.Children.Add(_status);
        _details = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        _details.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); body.Children.Add(_details);
        _downloadProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 12, 0, 4), Visibility = Visibility.Collapsed };
        _downloadProgress.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _downloadProgress.SetResourceReference(Control.BackgroundProperty, "BarBrush");
        body.Children.Add(_downloadProgress);
        _releaseNotes = new Button { Content = T("查看版本说明", "View release notes"), HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 8, 8, 4), Visibility = Visibility.Collapsed };
        _releaseNotes.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _releaseNotes.Click += (_, _) =>
        {
            if (_available?.ReleaseUrl is { } url && UpdateService.IsTrustedReleaseUrl(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };
        body.Children.Add(_releaseNotes);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Loaded += async (_, _) =>
        {
            if (_storeManaged) ShowInitialState();
            else if (initialResult is not null) ApplyResult(initialResult);
            else await CheckAsync();
        };
        Closed += (_, _) => { _operation?.Cancel(); if (service is null) _service.Dispose(); };
        PreviewKeyDown += HandleKey;
    }

    private string T(string zh, string en) => _language == "zh" ? zh : en;

    private void ShowInitialState()
    {
        if (_storeManaged)
        {
            _status.Text = T("此版本通过 Microsoft Store 安装。", "This version was installed from the Microsoft Store.");
            _details.Text = T("请在 Microsoft Store 的“库”中获取映窗更新。", "Get YingView updates from Library in the Microsoft Store.");
            _primary.Visibility = Visibility.Collapsed;
        }
        else
        {
            _status.Text = T("可以检查是否有适用于此 Windows 版本的更新。", "Check for an update for this Windows release.");
            _details.Text = T("只会提供映窗正式发布的 Windows 安装程序。", "Only official YingView Windows releases are offered.");
        }
    }

    private async Task PrimaryActionAsync()
    {
        if (_busy) return;
        if (_download is not null)
        {
            InstallAndRestartRequested?.Invoke(this, _download);
            return;
        }
        if (_available is not null)
        {
            await DownloadAsync();
            return;
        }
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        BeginOperation(T("正在检查…", "Checking…"), T("正在连接 GitHub 获取正式版本信息。", "Contacting GitHub for official release information."));
        var token = _operation!.Token;
        try
        {
            var result = await _service.CheckAsync(_current, _language, _storeManaged, token);
            if (token.IsCancellationRequested) { ShowInitialState(); return; }
            ApplyResult(result);
        }
        catch (OperationCanceledException) { ShowInitialState(); }
        finally { EndOperation(); }
    }

    private void ApplyResult(UpdateCheckResult result)
    {
        _available = result.Status == UpdateCheckStatus.UpdateAvailable ? result : null;
        _download = null;
        _releaseNotes.Visibility = Visibility.Collapsed;
        _primary.Visibility = _storeManaged ? Visibility.Collapsed : Visibility.Visible;
        _primary.Content = T("检查更新", "Check for updates");
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _status.Text = T($"发现新版本 {result.Version}", $"Version {result.Version} is available");
                _details.Text = string.IsNullOrWhiteSpace(result.ReleaseName) ? T("可下载此 Windows 更新。", "This Windows update is ready to download.") : result.ReleaseName;
                _primary.Content = T("下载更新", "Download update");
                _releaseNotes.Visibility = result.ReleaseUrl is not null && UpdateService.IsTrustedReleaseUrl(result.ReleaseUrl) ? Visibility.Visible : Visibility.Collapsed;
                break;
            case UpdateCheckStatus.UpToDate:
                _status.Text = T("当前已是最新版本。", "You have the latest version."); _details.Text = ""; break;
            case UpdateCheckStatus.StoreManaged:
                _status.Text = T("此版本通过 Microsoft Store 安装。", "This version was installed from the Microsoft Store.");
                _details.Text = T("请在 Microsoft Store 的“库”中获取映窗更新。", "Get YingView updates from Library in the Microsoft Store."); break;
            case UpdateCheckStatus.NoWindowsRelease:
                _status.Text = T("目前尚无可用的 Windows 更新。", "No Windows update is available yet.");
                _details.Text = T("GitHub 上尚无包含此语言 Windows 安装程序的正式版本。", "No official GitHub release includes a Windows installer for this language yet."); break;
            default:
                _status.Text = T("暂时无法检查更新。", "Could not check for updates.");
                _details.Text = T("请检查网络连接后重试。", "Check your connection and try again."); break;
        }
        _primary.IsEnabled = !_storeManaged;
    }

    private async Task DownloadAsync()
    {
        BeginOperation(T("正在下载并验证… 0%", "Downloading and verifying… 0%"), T("下载完成后会检查文件是否完整。", "The file will be checked for integrity after the download completes."));
        _downloadProgress.Value = 0;
        _downloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(ratio =>
        {
            if (!_busy) return;
            ratio = Math.Clamp(ratio, 0, 1);
            _downloadProgress.Value = ratio;
            _status.Text = T($"正在下载并验证… {ratio:P0}", $"Downloading and verifying… {ratio:P0}");
        });
        var token = _operation!.Token;
        try
        {
            _download = await _service.DownloadVerifiedAsync(_available!, token, progress);
            _downloadProgress.Visibility = Visibility.Collapsed;
            _status.Text = T("更新已下载并验证。", "The update was downloaded and verified.");
            _details.Text = T("可以安装此更新并重启映窗。", "This update is ready to install and restart YingView.");
            _primary.Content = T("安装并重启", "Install and restart");
            _primary.IsEnabled = InstallAndRestartRequested is not null;
        }
        catch (OperationCanceledException) { _downloadProgress.Visibility = Visibility.Collapsed; _status.Text = T("下载已取消。", "Download canceled."); _details.Text = T("可以重试下载。", "You can try the download again."); _primary.Content = T("重试下载", "Retry download"); }
        catch
        {
            _downloadProgress.Visibility = Visibility.Collapsed;
            _download = null;
            _status.Text = T("下载或完整性校验失败。", "Download or integrity check failed.");
            _details.Text = T("安装程序未通过验证，请重试。", "The installer did not pass verification. Please try again.");
            _primary.Content = T("重试下载", "Retry download");
        }
        finally { EndOperation(); }
    }

    private void BeginOperation(string status, string details)
    {
        _busy = true; _operation?.Dispose(); _operation = new CancellationTokenSource();
        _downloadProgress.Visibility = Visibility.Collapsed;
        _status.Text = status; _details.Text = details;
        _primary.IsEnabled = false; _cancel.Content = T("取消", "Cancel"); _cancel.IsEnabled = true;
    }

    private void EndOperation()
    {
        _busy = false; _operation?.Dispose(); _operation = null;
        _primary.IsEnabled = !_storeManaged && (_download is null || InstallAndRestartRequested is not null);
        _cancel.Content = T("关闭", "Close"); _cancel.IsEnabled = true;
        if (_available is not null && _download is null) _primary.Content = T("下载更新", "Download update");
    }

    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _busy) { _operation?.Cancel(); e.Handled = true; return; }
        if (e.Key == Key.Enter && !_busy && _primary.IsEnabled && _primary.Visibility == Visibility.Visible)
        { _ = PrimaryActionAsync(); e.Handled = true; }
        if (e.Key == Key.F5 && !_busy && !_storeManaged)
        { _available = null; _download = null; _ = CheckAsync(); e.Handled = true; }
    }
}
