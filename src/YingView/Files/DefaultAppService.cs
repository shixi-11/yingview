using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YingView.Files;

/// <summary>Registers YingView as a per-user candidate and opens Windows Default Apps settings.</summary>
public static class DefaultAppService
{
    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".bmp", ".tif", ".tiff", ".ico"];

    public sealed record Association(string Extension, string? ProgId, bool IsYingView);
    public sealed record RegistrationPlan(string Language, string DisplayName, string ProgId, string CapabilitiesPath,
        string ExecutablePath, IReadOnlyList<string> Extensions);
    public sealed record OpenResult(bool RegistrationSucceeded, bool SettingsLaunchSucceeded, string SettingsUri,
        string? Error = null);

    /// <summary>Pure, deterministic plan suitable for unit tests; accepts an explicit executable path.</summary>
    public static RegistrationPlan CreateRegistrationPlan(string language, string executablePath)
    {
        var lang = NormalizeLanguage(language);
        return new RegistrationPlan(lang, lang == "zh-CN" ? "映窗" : "YingView", $"YingView.{lang}",
            $@"Software\YingView\{lang}\Capabilities", Path.GetFullPath(executablePath), SupportedExtensions);
    }

    /// <summary>Builds the documented per-user Default Apps deep link, with a general-page fallback.</summary>
    public static string BuildSettingsUri(string registeredAppName) =>
        BuildSettingsUri(registeredAppName, "registeredAppUser");

    public static string BuildSettingsUri(string value, string parameter) =>
        "ms-settings:defaultapps?" + (parameter is "registeredAppUser" or "registeredAUMID" ? parameter : "registeredAppUser") + "=" + Uri.EscapeDataString(value);

    public static string BuildGeneralSettingsUri() => "ms-settings:defaultapps";

    /// <summary>Returns effective Windows associations for supported formats, not HKCU registry guesses.</summary>
    public static IReadOnlyList<Association> GetAssociations(string language)
    {
        var plan = CreateRegistrationPlan(language, Environment.ProcessPath ?? "YingView.exe");
        return plan.Extensions.Select(ext =>
        {
            var progId = QueryEffectiveProgId(ext);
            return new Association(ext, progId, string.Equals(progId, plan.ProgId, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    /// <summary>Registers a per-user candidate if needed, then opens its Windows Default Apps page.</summary>
    /// <remarks>Successful launch only means Settings opened; Windows still requires the user to choose formats.</remarks>
    public static OpenResult RegisterAndOpen(string language)
    {
        string uri;
        bool registered;
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
            var plan = CreateRegistrationPlan(language, exe);
            var packageFamily = GetCurrentPackageFamilyName();
            if (packageFamily is null) RegisterCandidate(plan);
            registered = true;
            uri = packageFamily is null ? BuildSettingsUri(plan.DisplayName) : BuildSettingsUri($"{packageFamily}!YingView", "registeredAUMID");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException)
        {
            var fallbackName = NormalizeLanguage(language) == "zh-CN" ? "映窗" : "YingView";
            uri = BuildSettingsUri(fallbackName);
            registered = false;
            var launched = LaunchSettings(uri);
            if (!launched) { uri = BuildGeneralSettingsUri(); launched = LaunchSettings(uri); }
            return new OpenResult(false, launched, uri, ex.Message);
        }

        var success = LaunchSettings(uri);
        if (!success) { uri = BuildGeneralSettingsUri(); success = LaunchSettings(uri); }
        return new OpenResult(registered, success, uri);
    }

    private static void RegisterCandidate(RegistrationPlan plan)
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true)!;
        using (var progId = classes.CreateSubKey(plan.ProgId, true)!)
        {
            progId.SetValue("", plan.DisplayName, RegistryValueKind.String);
            progId.SetValue("FriendlyTypeName", plan.DisplayName, RegistryValueKind.String);
            using var icon = progId.CreateSubKey("DefaultIcon", true)!;
            icon.SetValue("", $"\"{plan.ExecutablePath}\",0", RegistryValueKind.String);
            using var command = progId.CreateSubKey(@"shell\open\command", true)!;
            command.SetValue("", $"\"{plan.ExecutablePath}\" \"%1\"", RegistryValueKind.String);
        }

        using (var app = classes.CreateSubKey($@"Applications\{Path.GetFileName(plan.ExecutablePath)}", true)!)
        {
            app.SetValue("FriendlyAppName", plan.DisplayName, RegistryValueKind.String);
            using var supported = app.CreateSubKey("SupportedTypes", true)!;
            foreach (var extension in plan.Extensions) supported.SetValue(extension, "", RegistryValueKind.String);
            using var icon = app.CreateSubKey("DefaultIcon", true)!;
            icon.SetValue("", $"\"{plan.ExecutablePath}\",0", RegistryValueKind.String);
            using var command = app.CreateSubKey(@"shell\open\command", true)!;
            command.SetValue("", $"\"{plan.ExecutablePath}\" \"%1\"", RegistryValueKind.String);
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(plan.CapabilitiesPath, true)!)
        {
            capabilities.SetValue("ApplicationName", plan.DisplayName, RegistryValueKind.String);
            capabilities.SetValue("ApplicationIcon", $"\"{plan.ExecutablePath}\",0", RegistryValueKind.String);
            capabilities.SetValue("ApplicationDescription", plan.Language == "zh-CN" ? "本地图片查看与对照审阅" : "Local image viewing and comparison", RegistryValueKind.String);
            using var fileAssociations = capabilities.CreateSubKey("FileAssociations", true)!;
            foreach (var extension in plan.Extensions) fileAssociations.SetValue(extension, plan.ProgId, RegistryValueKind.String);
        }

        using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications", true)!;
        registered.SetValue(plan.DisplayName, plan.CapabilitiesPath, RegistryValueKind.String);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static string? QueryEffectiveProgId(string extension)
    {
        const uint flags = 0x00000000; // ASSOCF_NONE
        const uint str = 20; // ASSOCSTR_PROGID
        var length = 0;
        _ = AssocQueryString(flags, str, extension, "open", null, ref length);
        if (length <= 1) return null;
        var buffer = new StringBuilder(length);
        return AssocQueryString(flags, str, extension, "open", buffer, ref length) == 0 ? buffer.ToString() : null;
    }

    private static bool LaunchSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return true; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { return false; }
    }

    private static string NormalizeLanguage(string language) =>
        language.Equals("zh", StringComparison.OrdinalIgnoreCase) || language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";

    private static string? GetCurrentPackageFamilyName()
    {
        var length = 0;
        var result = GetCurrentPackageFamilyNameNative(ref length, null);
        if (result == 15700) return null; // APPMODEL_ERROR_NO_PACKAGE
        if (result != 122 || length <= 1) return null; // ERROR_INSUFFICIENT_BUFFER
        var buffer = new StringBuilder(length);
        return GetCurrentPackageFamilyNameNative(ref length, buffer) == 0 ? buffer.ToString() : null;
    }

    [DllImport("Shlwapi.dll", EntryPoint = "AssocQueryStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryString(uint flags, uint str, string association, string? extra,
        StringBuilder? outputString, ref int outputLength);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentPackageFamilyName", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyNameNative(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
