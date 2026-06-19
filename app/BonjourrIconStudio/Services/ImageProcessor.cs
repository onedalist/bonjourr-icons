using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonjourrIconStudio.Helpers;
using BonjourrIconStudio.Models;
using ImageMagick;

namespace BonjourrIconStudio.Services;

public sealed record LoadedImage(BitmapSource Preview, int Width, int Height);

public sealed class ImageProcessor
{
    private const int Oversampling = 4;

    public LoadedImage LoadForEditing(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();

        var originalWidth = checked((int)image.Width);
        var originalHeight = checked((int)image.Height);

        if (image.Width > 2048 || image.Height > 2048)
            image.Thumbnail(2048, 2048);

        image.ColorSpace = ColorSpace.sRGB;
        image.Format = MagickFormat.Bgra;
        var pixels = image.ToByteArray();
        var stride = checked((int)image.Width * 4);
        var bitmap = BitmapSource.Create(
            checked((int)image.Width),
            checked((int)image.Height),
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        return new LoadedImage(bitmap, originalWidth, originalHeight);
    }

    public Task<IReadOnlyList<string>> ExportAsync(
        ExportRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() => Export(request, progress, cancellationToken), cancellationToken);

    private static IReadOnlyList<string> Export(
        ExportRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.DestinationFolder);
        var results = new List<string>();
        var crop = CalculateCrop(request.Crop);

        using var source = new MagickImage(request.Crop.SourcePath);
        source.AutoOrient();
        source.ColorSpace = ColorSpace.sRGB;
        source.Crop(new MagickGeometry(crop.X, crop.Y, crop.Size, crop.Size));
        source.ResetPage();

        foreach (var size in request.Sizes.OrderBy(value => value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Подготавливаю {size} px…");

            var workingSize = checked((uint)(size * Oversampling));
            using var prepared = source.Clone();
            prepared.FilterType = FilterType.Lanczos;
            prepared.Resize(workingSize, workingSize);
            prepared.Alpha(AlphaOption.On);

            using (var mask = CreateMask(checked((int)workingSize), request.AddSoftEdgeHighlight))
                prepared.Composite(mask, CompositeOperator.CopyAlpha);

            prepared.FilterType = FilterType.Lanczos;
            prepared.Resize((uint)size, (uint)size);
            prepared.ColorSpace = ColorSpace.sRGB;
            prepared.Strip();

            foreach (var format in request.Formats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = format switch
                {
                    IconFormat.Png => "png",
                    IconFormat.WebP => "webp",
                    IconFormat.Avif => "avif",
                    _ => throw new ArgumentOutOfRangeException()
                };
                var outputPath = Path.Combine(request.DestinationFolder, $"{request.BaseName}_{size}.{extension}");

                using var output = prepared.Clone();
                switch (format)
                {
                    case IconFormat.Png:
                        output.Format = MagickFormat.Png;
                        output.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                        break;
                    case IconFormat.WebP:
                        output.Format = MagickFormat.WebP;
                        output.Quality = (uint)Math.Clamp(request.WebPQuality, 1, 100);
                        output.Settings.SetDefine(MagickFormat.WebP, "method", "6");
                        break;
                    case IconFormat.Avif:
                        output.Format = MagickFormat.Avif;
                        output.Quality = (uint)Math.Clamp(request.AvifQuality, 1, 100);
                        break;
                }

                output.Write(outputPath);
                results.Add(outputPath);
                progress?.Report($"Готово: {Path.GetFileName(outputPath)}");
            }
        }

        return results;
    }

    private static (int X, int Y, uint Size) CalculateCrop(CropState state)
    {
        var displayedWidth = state.OrientedWidth * state.DisplayScale;
        var displayedHeight = state.OrientedHeight * state.DisplayScale;
        var left = (state.ViewportSize - displayedWidth) / 2d + state.OffsetX;
        var top = (state.ViewportSize - displayedHeight) / 2d + state.OffsetY;

        var cropSize = state.ViewportSize / state.DisplayScale;
        var maxCropSize = Math.Min(state.OrientedWidth, state.OrientedHeight);
        cropSize = Math.Min(cropSize, maxCropSize);

        var x = Math.Clamp(-left / state.DisplayScale, 0, state.OrientedWidth - cropSize);
        var y = Math.Clamp(-top / state.DisplayScale, 0, state.OrientedHeight - cropSize);

        var size = Math.Max(1, (int)Math.Round(cropSize));
        var intX = Math.Clamp((int)Math.Round(x), 0, Math.Max(0, state.OrientedWidth - size));
        var intY = Math.Clamp((int)Math.Round(y), 0, Math.Max(0, state.OrientedHeight - size));
        return (intX, intY, checked((uint)size));
    }

    private static MagickImage CreateMask(int size, bool addEdgeHighlight)
    {
        var pixels = new byte[checked(size * size * 4)];
        var half = size / 2d;

        for (var y = 0; y < size; y++)
        {
            var normalizedY = (y + 0.5d - half) / half;
            for (var x = 0; x < size; x++)
            {
                var normalizedX = (x + 0.5d - half) / half;
                var inside = SquircleGeometry.ContainsNormalized(normalizedX, normalizedY);
                var index = (y * size + x) * 4;
                pixels[index] = 255;
                pixels[index + 1] = 255;
                pixels[index + 2] = 255;
                pixels[index + 3] = inside ? (byte)255 : (byte)0;
            }
        }

        var bitmap = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new MagickImage(stream);
    }
}
