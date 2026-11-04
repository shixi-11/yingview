using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YingView;

internal sealed class AboutWindow : Window
{
    internal const string CollaborationEmail = "info@elevencapital.ltd";
    private readonly bool _chinese;
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private string T(string zh, string en) => _chinese ? zh : en;
    internal static string SupportUrl(bool chinese) => $"https://shixilin.com/{(chinese ? "zh" : "en")}/support";

    internal AboutWindow(bool chinese, string version, Action showLicenses)
    {
        _chinese = chinese;
        Title = T("关于映窗", "About YingView");
        Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        SetResourceReference(BackgroundProperty, "ShellBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel();
        var frame = new Border { Padding = new Thickness(24), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "ShellBrush");
        frame.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        frame.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        frame.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13d);
        Content = frame;
        panel.Children.Add(new TextBlock { Text = T("映窗", "YingView"), FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = version + T("  预览版", "  Preview"), Margin = new Thickness(0, 4, 0, 14) });
        panel.Children.Add(new TextBlock { Text = T("由光之十一开发", "Created by Shixi Lin"), Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = T("完整看图、并排对照，随手调整观看背景。图片在本机处理，无广告，不上传图片。", "See whole images, compare details, and adjust your viewing background. Local image processing, no ads and no image uploads."), TextWrapping = TextWrapping.Wrap });
        var links = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        links.Children.Add(LinkButton(T("查看源码", "Source code"), $"https://github.com/shixi-11/yingview/tree/v{version}", "SourceLink"));
        var license = new Button { Name = "LicenseLink", Content = "AGPLv3", Padding = new Thickness(12, 7, 12, 7) };
        license.Click += (_, _) => showLicenses(); links.Children.Add(license); panel.Children.Add(links);
        var support = LinkButton(T("自愿支持映窗", "Support YingView"), SupportUrl(chinese), "SupportLink");
        support.HorizontalAlignment = HorizontalAlignment.Left; support.Margin = new Thickness(0, 16, 0, 6);
        support.SetResourceReference(BackgroundProperty, "AccentBrush"); support.SetResourceReference(ForegroundProperty, "AccentTextBrush"); panel.Children.Add(support);
        panel.Children.Add(new TextBlock { Text = T("打赏完全自愿，不影响任何功能的使用。", "Donations are optional. All features work without donating."), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = T("合作联系", "Collaboration"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 4) });
        var contact = new WrapPanel();
        contact.Children.Add(LinkButton(CollaborationEmail, "mailto:" + CollaborationEmail, "ContactLink"));
        var copy = new Button { Name = "CopyContact", Content = T("复制", "Copy"), Padding = new Thickness(9, 7, 9, 7) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(CollaborationEmail); _status.Text = T("邮箱地址已复制。", "Email address copied."); }
            catch { _status.Text = T("暂时无法复制，请手动复制邮箱地址。", "Could not copy the address. Please copy it manually."); }
        };
        contact.Children.Add(copy); panel.Children.Add(contact);
        panel.Children.Add(new TextBlock { Text = T("按 AGPLv3 提供，不提供担保。你可按许可证修改和再分发。第三方组件保留各自许可。", "Provided under AGPLv3 without warranty. You may modify and redistribute it under the license. Third-party components retain their own terms."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(_status);
        var close = new Button { Content = T("关闭", "Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(close);
    }
    private Button LinkButton(string text, string url, string name)
    {
        var button = new Button { Name = name, Content = text, Tag = url, Padding = new Thickness(10, 7, 10, 7) };
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { _status.Text = T("暂时无法打开链接，请稍后重试。", "Could not open the link. Please try again."); }
        };
        return button;
    }
}
