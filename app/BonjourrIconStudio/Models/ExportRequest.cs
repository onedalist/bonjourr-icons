namespace BonjourrIconStudio.Models;

public enum IconFormat
{
    Png,
    WebP,
    Avif
}

public sealed record CropState(
    string SourcePath,
    int OrientedWidth,
    int OrientedHeight,
    double ViewportSize,
    double DisplayScale,
    double OffsetX,
    double OffsetY);

public sealed record ExportRequest(
    CropState Crop,
    string DestinationFolder,
    string BaseName,
    IReadOnlyCollection<int> Sizes,
    IReadOnlyCollection<IconFormat> Formats,
    int WebPQuality,
    int AvifQuality,
    bool AddSoftEdgeHighlight);
