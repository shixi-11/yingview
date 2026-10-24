using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows.Media;

namespace YingView;

public sealed class Preferences
{
    private readonly string? _storagePath;
    public Preferences() { }
    internal Preferences(string storagePath) { _storagePath = storagePath; }
    public string Language { get; set; } = ClientBranding.InitialLanguage;
    public string Background { get; set; } = "black";
    public string DefaultFit { get; set; } = "contain";
    public bool Filmstrip { get; set; }
    public int AppearanceUiVersion { get; set; }
    public bool FullscreenOnOpen { get; set; }
    public ViewerTheme Theme { get; set; } = ViewerTheme.BuiltIn[0];
    public double TextScale { get; set; } = 1.0;
    public bool CompactToolbar { get; set; }
    public List<ViewerTheme> PersonalStyles { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
    private static string DefaultFileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "settings.json");
    private string FileName => _storagePath ?? DefaultFileName;
    public static Preferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(DefaultFileName)) ?? new();
            value.Theme.Validate();
            value.DefaultFit = "contain";
            value.Language = value.Language == "zh" ? "zh" : "en";
            if (ClientBranding.DistributionLanguage != "auto") value.Language = ClientBranding.InitialLanguage;
            if (value.Background == "dark") value.Background = "black";
            if (value.Background is not ("theme" or "light" or "checker" or "black" or "white")) value.Background = "black";
            value.RecentFiles = (value.RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(8).ToList();
            if (value.AppearanceUiVersion < 2) { value.Filmstrip = false; value.AppearanceUiVersion = 2; }
            if (value.TextScale is < 0.85 or > 1.3) value.TextScale = 1.0;
            value.PersonalStyles ??= [];
            value.PersonalStyles = value.PersonalStyles.Where(t => { try { t.Validate(); return true; } catch { return false; } }).ToList();
            return value;
        }
        catch { return new() { AppearanceUiVersion = 2 }; }
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
    public static string LocalizedName(string name, bool chinese) => !chinese ? name : name switch
    { "Obsidian" => "曜石黑", "Paper" => "纸白", "Moss" => "苔绿", "Iris" => "鸢紫", _ => name };
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
