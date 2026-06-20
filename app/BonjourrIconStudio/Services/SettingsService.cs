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
            settings.CustomShapeExponent = Math.Clamp(settings.CustomShapeExponent, 2d, 8d);
            settings.LiquidGlassThickness = Math.Clamp(settings.LiquidGlassThickness, 0.5d, 4d);
            settings.SavedShapes ??= [];
            settings.SavedShapes = settings.SavedShapes
                .Where(shape => !string.IsNullOrWhiteSpace(shape.Name))
                .Select(shape => new SavedShapePreset
                {
                    Id = string.IsNullOrWhiteSpace(shape.Id) ? Guid.NewGuid().ToString("N") : shape.Id,
                    Name = shape.Name.Trim(),
                    Exponent = Math.Clamp(shape.Exponent, 2d, 8d)
                })
                .ToList();
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
