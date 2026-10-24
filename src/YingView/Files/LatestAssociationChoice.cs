// Registry context follows MIT-licensed cssxn/UserChoiceLatestHash, commit 662070e5cdea4496bc474f4bdfd81f77d4cabab3.
// Copyright (c) 2026 cssxn. See ThirdParty/UserChoiceLatestHash.txt.
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using YingView.Files.AssociationHash;

namespace YingView.Files;

internal sealed class LatestAssociationChoice(string sid, string machineId)
{
    private const string FileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    private static readonly string[] Salts =
    [
        "Copyright (C) Microsoft. All rights reserved {3822B7CA-C2F4-4889-B8CC-4CE39A8FB81C}",
        "Copyright (C) Microsoft. All rights reserved {D185E0A1-E265-4724-AA21-3A17B038D72E}",
        "Copyright (C) Microsoft. All rights reserved {97B6BCF4-C367-4577-95BE-73BD3053A5E0}"
    ];

    internal static LatestAssociationChoice? OpenVerified(string sid, IReadOnlyList<DefaultAppService.Association> effective)
    {
        var present = effective.Any(a =>
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{FileExts}\{a.Extension}\UserChoiceLatest");
            return key?.GetValue("Hash") is string;
        });
        if (!present) return null;
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient");
        var machineId = (machine?.GetValue("MachineID") as string)?.Trim('{', '}');
        if (string.IsNullOrEmpty(machineId)) throw new NotSupportedException("Cannot verify the current machine's association context.");
        var writer = new LatestAssociationChoice(sid, machineId);
        if (!effective.Any(a => writer.Matches(a.Extension)))
            throw new NotSupportedException("The current UserChoiceLatest hash format could not be verified.");
        return writer;
    }

    internal bool Matches(string extension, string? requiredProgId = null)
    {
        var verified = ReadVerifiedProgId(extension);
        return verified is not null && (requiredProgId is null || string.Equals(verified, requiredProgId, StringComparison.OrdinalIgnoreCase));
    }

    internal string? ReadVerifiedProgId(string extension)
    {
        using var parent = Registry.CurrentUser.OpenSubKey($@"{FileExts}\{extension}\UserChoiceLatest");
        using var choice = parent?.OpenSubKey("ProgId");
        if (parent?.GetValue("Hash") is not string hash || choice?.GetValue("ProgId") is not string progId) return null;
        return string.Equals(hash, Calculate(extension, progId, LegacyAssociationChoice.ReadLastWriteTime(choice)), StringComparison.Ordinal) ? progId : null;
    }

    internal void Write(string extension, string progId)
    {
        using var parent = Registry.CurrentUser.CreateSubKey($@"{FileExts}\{extension}\UserChoiceLatest", true)!;
        using var choice = parent.CreateSubKey("ProgId", true)!;
        choice.SetValue("ProgId", progId, RegistryValueKind.String);
        var timestamp = LegacyAssociationChoice.ReadLastWriteTime(choice);
        var hash = Calculate(extension, progId, timestamp);
        if (timestamp != LegacyAssociationChoice.ReadLastWriteTime(choice)
            || !string.Equals(choice.GetValue("ProgId") as string, progId, StringComparison.Ordinal))
            throw new IOException("The image association changed while it was being updated.");
        parent.SetValue("Hash", hash, RegistryValueKind.String);
    }

    private string Calculate(string extension, string progId, long timestamp) =>
        LatestAssociationHash.Compute(BuildCanonical(extension, sid, progId, timestamp, machineId));

    internal static string BuildCanonical(string extension, string userSid, string progId, long timestamp, string machine)
    {
        machine = machine.Trim('{', '}');
        var modClass = machine[^1] % 3;
        var time = (timestamp - timestamp % TimeSpan.TicksPerMillisecond).ToString("x16", CultureInfo.InvariantCulture);
        var salt = Salts[modClass];
        return (modClass switch
        {
            0 => salt + extension + time + machine + progId + userSid,
            1 => salt + time + extension + userSid + machine + progId,
            _ => userSid + time + salt + extension + machine + progId
        }).ToLowerInvariant();
    }
}
