using System.Globalization;
using System.Reflection;

namespace YingView;

internal static class ClientBranding
{
    internal static string DistributionLanguage { get; } = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ClientLanguage")?.Value ?? "auto";
    internal static string ResolveLanguage(string? distribution, string? systemLanguage) => distribution switch
    {
        "zh-CN" => "zh",
        "en-US" => "en",
        _ => systemLanguage is not null && (systemLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            systemLanguage.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) || systemLanguage.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            systemLanguage.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)) ? "zh" : "en"
    };
    internal static string InitialLanguage => ResolveLanguage(DistributionLanguage, CultureInfo.CurrentUICulture.Name);
    internal static string DisplayName(string language) => language == "zh" ? "映窗" : "YingView";
    internal static string IconResource(string language) => language == "zh" ? "yingview-zh-CN.ico" : "yingview-en-US.ico";
}
