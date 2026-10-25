using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace YingView.Updates;

internal static class UpdateInstaller
{
    internal static async Task VerifyBeforeRestartAsync(VerifiedUpdateDownload update)
    {
        var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "updates")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(update.FilePath).StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            update.Version <= typeof(App).Assembly.GetName().Version || App.IsStoreManaged)
            throw new InvalidDataException("The update is not applicable.");
        await using var file = File.OpenRead(update.FilePath);
        var actual = await SHA256.HashDataAsync(file);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(update.Sha256)))
            throw new InvalidDataException("The update changed after verification.");
        var info = FileVersionInfo.GetVersionInfo(update.FilePath);
        var expectedLanguage = ClientBranding.DistributionLanguage == "zh-CN" ? "YingView-zh-CN.exe" :
            ClientBranding.DistributionLanguage == "en-US" ? "YingView-en-US.exe" : ClientBranding.InitialLanguage == "zh" ? "YingView-zh-CN.exe" : "YingView-en-US.exe";
        if (update.AssetName != expectedLanguage || !Version.TryParse(info.FileVersion, out var fileVersion) ||
            fileVersion.Major != update.Version.Major || fileVersion.Minor != update.Version.Minor || fileVersion.Build != update.Version.Build)
            throw new InvalidDataException("The update version or language does not match its release.");
    }

    internal static async Task InstallSelfAndRestartAsync(string? openPath)
    {
        if (App.IsStoreManaged) throw new InvalidOperationException("Store installations use Store updates.");
        var stage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "updates", "install-" + Guid.NewGuid().ToString("N"));
        PrepareInstallation(stage, Environment.ProcessPath ?? throw new IOException("Application path unavailable."));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(stage, "Install.ps1"), "-Source", stage, "-RestartRunningVersion" })
            start.ArgumentList.Add(arg);
        if (openPath is not null && File.Exists(openPath)) { start.ArgumentList.Add("-OpenPath"); start.ArgumentList.Add(openPath); }
        using var process = Process.Start(start) ?? throw new IOException("Installer unavailable.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output; await errors;
        if (process.ExitCode != 0) throw new IOException("Installer did not complete.");
    }

    internal static void PrepareInstallation(string stage, string executable)
    {
        Directory.CreateDirectory(stage);
        File.Copy(executable, Path.Combine(stage, "YingView.exe"), true);
        var assembly = typeof(App).Assembly;
        var files = new Dictionary<string, string>
        {
            ["LICENSE"] = "YingView.LICENSE", ["NOTICE"] = "YingView.NOTICE",
            ["CLIENT-LICENSE.txt"] = "YingView.CLIENT-LICENSE", ["CLIENT-LICENSE.zh-CN.txt"] = "YingView.CLIENT-LICENSE.zh-CN",
            ["LICENSING.md"] = "YingView.LICENSING", ["THIRD-PARTY-NOTICES.md"] = "YingView.THIRD-PARTY-NOTICES",
            ["Install.ps1"] = "YingView.UpdateInstaller.ps1", ["ShortcutIdentity.cs"] = "YingView.ShortcutIdentity.cs"
        };
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("YingView.ThirdParty.", StringComparison.Ordinal)))
            files[Path.Combine("ThirdParty", resource["YingView.ThirdParty.".Length..])] = resource;
        foreach (var (filename, resource) in files)
        {
            var target = Path.Combine(stage, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = assembly.GetManifestResourceStream(resource) ?? throw new IOException("Installation resource missing.");
            using var output = File.Create(target); input.CopyTo(output);
        }
        var version = assembly.GetName().Version!.ToString(3);
        var language = ClientBranding.InitialLanguage == "zh" ? "zh-CN" : "en-US";
        File.WriteAllText(Path.Combine(stage, "client-language.json"), JsonSerializer.Serialize(new { Language = language, DisplayName = ClientBranding.DisplayName(ClientBranding.InitialLanguage), Version = version }));
    }
}
