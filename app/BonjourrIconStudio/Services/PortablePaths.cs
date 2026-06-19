namespace BonjourrIconStudio.Services;

public static class PortablePaths
{
    public static string RootFolder => AppContext.BaseDirectory;
    public static string DataFolder => Path.Combine(RootFolder, "Data");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string VaultFile => Path.Combine(DataFolder, "github-profile.enc");
    public static string DefaultExportFolder => Path.Combine(RootFolder, "Exports");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(DefaultExportFolder);
    }
}
