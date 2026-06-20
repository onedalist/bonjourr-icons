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
    public string SelectedShapeId { get; set; } = "apple-27";
    public double CustomShapeExponent { get; set; } = 4.2;
    public bool LiquidGlassEnabled { get; set; }
    public double LiquidGlassThickness { get; set; } = 1.0;
    public List<SavedShapePreset> SavedShapes { get; set; } = [];
}

public sealed class SavedShapePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public double Exponent { get; set; } = 4.2;
}
