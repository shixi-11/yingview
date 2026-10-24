using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using YingView.Files.AssociationHash;

namespace YingView.Files;

/// <summary>Standalone compatibility writer, enabled only when live effective choices validate.</summary>
internal sealed class LegacyAssociationChoice
{
    private const string FileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    private const string Experience = "User Choice set via Windows User Experience {D18B6DD5-6124-4341-9318-804003BAFA0B}";
    private readonly string _sid;
    private LegacyAssociationChoice(string sid) => _sid = sid;

    internal static LegacyAssociationChoice OpenVerified(IReadOnlyList<DefaultAppService.Association> effective)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new NotSupportedException("Cannot identify the signed-in user.");
        var writer = new LegacyAssociationChoice(sid);
        // A stale UserChoice key alone does not prove that this Windows profile uses its hash scheme.
        // Require both an exact mathematical match and agreement with the effective Shell handler.
        if (!effective.Any(a => writer.MatchesEffective(a)))
            throw new NotSupportedException("The effective Windows choice format could not be verified.");
        return writer;
    }

    internal bool MatchesEffective(DefaultAppService.Association effective)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{FileExts}\{effective.Extension}\UserChoice");
        if (key?.GetValue("ProgId") is not string progId || key.GetValue("Hash") is not string hash
            || !string.Equals(progId, effective.ProgId, StringComparison.OrdinalIgnoreCase)) return false;
        try { return string.Equals(hash, Calculate(effective.Extension, progId, ReadLastWriteTime(key)), StringComparison.Ordinal); }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    internal void Write(string extension, string progId)
    {
        if (!extension.StartsWith('.') || extension.Contains('\\') || extension.Contains('/') || string.IsNullOrWhiteSpace(progId))
            throw new ArgumentException("Invalid image association.");
        using var parent = Registry.CurrentUser.CreateSubKey($@"{FileExts}\{extension}", true)!;
        parent.DeleteSubKey("UserChoice", throwOnMissingSubKey: false);
        using var choice = parent.CreateSubKey("UserChoice", true)!;
        choice.SetValue("ProgId", progId, RegistryValueKind.String);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var timestamp = ReadLastWriteTime(choice);
            choice.SetValue("Hash", Calculate(extension, progId, timestamp), RegistryValueKind.String);
            // Hash writes update the key time too; crossing a minute requires a fresh calculation.
            if (Minute(timestamp) == Minute(ReadLastWriteTime(choice))) return;
        }
        throw new IOException("Could not complete association within a stable clock minute.");
    }

    private string Calculate(string extension, string progId, long fileTime) =>
        LegacyAssociationHash.Compute(BuildCanonical(extension, _sid, progId, fileTime));

    internal static string BuildCanonical(string extension, string sid, string progId, long fileTime) =>
        (extension + sid + progId + Minute(fileTime).ToString("x16", CultureInfo.InvariantCulture) + Experience).ToLowerInvariant();

    private static long Minute(long fileTime) => fileTime - fileTime % TimeSpan.TicksPerMinute;

    private static long ReadLastWriteTime(RegistryKey key)
    {
        var z = IntPtr.Zero;
        var error = RegQueryInfoKey(key.Handle, z, z, z, z, z, z, z, z, z, z, out var time);
        if (error != 0) throw new System.ComponentModel.Win32Exception(error);
        return time;
    }

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", ExactSpelling = true)]
    private static extern int RegQueryInfoKey(SafeRegistryHandle key, IntPtr keyClass, IntPtr classLength,
        IntPtr reserved, IntPtr subKeys, IntPtr maxSubKeyLength, IntPtr maxClassLength, IntPtr values,
        IntPtr maxValueNameLength, IntPtr maxValueLength, IntPtr securityLength, out long lastWriteTime);
}
