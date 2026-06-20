namespace BonjourrIconStudio.Models;

public sealed class AppSettings
{
    public string RepositoryOwner { get; set; } = "onedalist";
    public string RepositoryName { get; set; } = "bonjourr-icons";
    public string Branch { get; set; } = "main";
    public string RepositoryFolder { get; set; } = "icons";
    public string ExportFolder { get; set; } = string.Empty;
    public int WebPQuality { get; set; } = 92;
    public int AvifQuality { get; set; } = 72;
    public bool UsePortableDataFolder { get; set; } = true;
    public bool LiquidGlassEnabled { get; set; }
    public LiquidGlassVariant LiquidGlassVariant { get; set; } = LiquidGlassVariant.Light;
}
