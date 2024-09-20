namespace Starterkit._keenthemes.libs;

// Base type class for theme settings
class KTThemeBase
{
    public string LayoutDir { get; set; } = String.Empty;

    public string Direction { get; set; } = String.Empty;

    public bool ModeSwitchEnabled { get; set; } = false;

    public string ModeDefault { get; set; } = String.Empty;

    public string AssetsDir { get; set; } = String.Empty;

    public string IconsType { get; set; } = String.Empty;

    public KTThemeAssets Assets { get; set; } = new KTThemeAssets();

    public SortedDictionary<string, SortedDictionary<string, string[]>> Vendors { get; set; } = new SortedDictionary<string, SortedDictionary<string, string[]>>();
}
