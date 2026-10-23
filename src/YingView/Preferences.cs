using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows.Media;

namespace YingView;

public sealed class Preferences
{
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
    public string Background { get; set; } = "dark";
    public string DefaultFit { get; set; } = "contain";
    public bool Filmstrip { get; set; } = true;
    public bool FullscreenOnOpen { get; set; }
    public ViewerTheme Theme { get; set; } = ViewerTheme.BuiltIn[0];
    public List<string> RecentFiles { get; set; } = [];
    private static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "settings.json");
    public static Preferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FileName)) ?? new();
            value.Theme.Validate();
            return value;
        }
        catch { return new(); }
    }
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
            var temporary = FileName + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FileName, true);
            return true;
        }
        catch { return false; }
    }
}

public sealed record ViewerTheme(string Name, string Shell, string Toolbar, string Text, string Muted, string Accent, string Canvas)
{
    public static readonly ViewerTheme[] BuiltIn = [
        new("Obsidian", "#111417", "#191D21", "#EDF0F2", "#A9B1B8", "#91D9C5", "#0C0E10"),
        new("Paper", "#F5F5F3", "#FFFFFF", "#24282D", "#646B72", "#28766C", "#E7E7E4"),
        new("Moss", "#161D1B", "#1F2A25", "#EEF2E9", "#B1C0B2", "#B0D1A0", "#111713"),
        new("Iris", "#1B1822", "#26212E", "#F0ECF8", "#BEB2CE", "#C8AFE9", "#131019")
    ];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60) throw new InvalidDataException("Theme name is invalid.");
        foreach (var value in new[] { Shell, Toolbar, Text, Muted, Accent, Canvas })
            if (value is null || !System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$"))
                throw new InvalidDataException("Theme colors must use #RRGGBB.");
    }
    public static SolidColorBrush Brush(string value)
    {
        var result = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); result.Freeze(); return result;
    }
}
