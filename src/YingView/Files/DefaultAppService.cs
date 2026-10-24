using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace YingView.Files;

/// <summary>Registers, sets and verifies per-user image associations.</summary>
public static class DefaultAppService
{
    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".bmp", ".tif", ".tiff", ".ico"];

    public sealed record Association(string Extension, string? ProgId, bool IsYingView);
    public sealed record RegistrationPlan(string Language, string DisplayName, string ProgId, string CapabilitiesPath,
        string ExecutablePath, IReadOnlyList<string> Extensions);
    public sealed record OpenResult(bool RegistrationSucceeded, bool SettingsLaunchSucceeded, string SettingsUri,
        string? Error = null);
    public sealed record BatchResult(IReadOnlyList<Association> Associations, string BackupPath,
        IReadOnlyDictionary<string, string> Errors)
    {
        public bool AllSet => Associations.Count > 0 && Associations.All(a => a.IsYingView);
    }
    private sealed record PreviousAssociation(string Extension, string? EffectiveProgId,
        string? ClassicProgId, string? UserChoiceProgId);

    // Store packages use the supported Windows Settings flow instead of registry compatibility writes.
    public static bool SupportsBatchSetup => GetCurrentPackageFamilyName() is null;

    /// <summary>Call only after explicit user confirmation. No elevation or system-protection changes.</summary>
    public static BatchResult SetAllDefaults(string language) => SetAllDefaults(CreateRegistrationPlan(language,
        Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.")));

    internal static BatchResult SetAllDefaults(RegistrationPlan plan)
    {
        if (!SupportsBatchSetup) throw new InvalidOperationException("Packaged apps must use Windows Settings.");
        if (!File.Exists(plan.ExecutablePath)) throw new FileNotFoundException("The viewer executable is missing.", plan.ExecutablePath);

        // Verify the actual profile before any compatibility writes; new or unknown hash formats fail closed.
        var effectiveBefore = GetAssociations(plan.Language);
        var choiceWriter = LegacyAssociationChoice.OpenVerified(effectiveBefore);

        var previous = plan.Extensions.Select(extension =>
        {
            using var classic = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}");
            using var choice = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
            return new PreviousAssociation(extension, QueryEffectiveProgId(extension),
                classic?.GetValue("") as string, choice?.GetValue("ProgId") as string);
        }).ToArray();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "Associations");
        Directory.CreateDirectory(folder);
        var backupPath = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json");
        // Save before changing any associations. Protected UserChoice hashes are deliberately not replayed.
        File.WriteAllText(backupPath, JsonSerializer.Serialize(new { CreatedUtc = DateTime.UtcNow, plan, Previous = previous },
            new JsonSerializerOptions { WriteIndented = true }));
        RegisterCandidate(plan);
        var errors = new Dictionary<string, string>();
        foreach (var old in previous)
        {
            if (string.Equals(old.EffectiveProgId, plan.ProgId, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                choiceWriter.Write(old.Extension, plan.ProgId);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                errors[old.Extension] = ex.Message;
                // A rejected change must not intentionally leave a deleted default in its place.
                if (old.EffectiveProgId is { Length: > 0 } original && original != "Unknown")
                {
                    try { choiceWriter.Write(old.Extension, original); }
                    catch (Exception restoreError) when (restoreError is UnauthorizedAccessException or IOException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
                    { errors[old.Extension] += " Restore: " + restoreError.Message; }
                }
            }
        }
        SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED, SHCNF_FLUSH
        var associations = GetAssociations(plan.Language);
        for (var attempt = 0; attempt < 8 && associations.Any(a => !a.IsYingView); attempt++)
        {
            Thread.Sleep(250);
            associations = GetAssociations(plan.Language);
        }
        foreach (var association in associations.Where(a => !a.IsYingView))
            errors.TryAdd(association.Extension, "Windows retained another effective default application.");
        return new BatchResult(associations, backupPath, errors);
    }

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
            progId.SetValue("AppUserModelID", ClientBranding.DesktopAppId(plan.Language == "zh-CN" ? "zh" : "en"), RegistryValueKind.String);
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
        foreach (var extension in plan.Extensions)
        {
            using var openWith = classes.CreateSubKey($@"{extension}\OpenWithProgids", true)!;
            openWith.SetValue(plan.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
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
