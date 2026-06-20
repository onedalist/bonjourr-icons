using System.Text.Json;
using BonjourrIconStudio.Models;

namespace BonjourrIconStudio.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Load()
    {
        PortablePaths.EnsureFolders();

        if (!File.Exists(PortablePaths.SettingsFile))
        {
            return new AppSettings { ExportFolder = PortablePaths.DefaultExportFolder };
        }

        try
        {
            var json = File.ReadAllText(PortablePaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.ExportFolder))
                settings.ExportFolder = PortablePaths.DefaultExportFolder;
            if (!Enum.IsDefined(settings.LiquidGlassVariant))
                settings.LiquidGlassVariant = LiquidGlassVariant.Light;
            return settings;
        }
        catch
        {
            return new AppSettings { ExportFolder = PortablePaths.DefaultExportFolder };
        }
    }

    public void Save(AppSettings settings)
    {
        PortablePaths.EnsureFolders();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(PortablePaths.SettingsFile, json);
    }
}
