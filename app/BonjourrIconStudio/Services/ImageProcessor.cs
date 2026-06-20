using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonjourrIconStudio.Helpers;
using BonjourrIconStudio.Models;
using ImageMagick;

namespace BonjourrIconStudio.Services;

public sealed record LoadedImage(BitmapSource Preview, int Width, int Height);

public sealed class ImageProcessor
{
    private const int Oversampling = 8;

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

            using (var mask = CreateMask(checked((int)workingSize)))
                prepared.Composite(mask, CompositeOperator.CopyAlpha);

            if (request.AddLiquidGlassOutline)
            {
                using var outline = CreateLiquidGlassOutline(
                    checked((int)workingSize),
                    request.LiquidGlassThickness * size / 128d * Oversampling,
                    request.LiquidGlassVariant);
                prepared.Composite(outline, CompositeOperator.Over);
            }

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

    private static MagickImage CreateMask(int size)
    {
        var pixels = new byte[checked(size * size * 4)];
        var half = size / 2d;
        var poweredCoordinates = new double[size];

        for (var coordinate = 0; coordinate < size; coordinate++)
        {
            var normalized = Math.Abs((coordinate + 0.5d - half) / half);
            poweredCoordinates[coordinate] = Math.Pow(normalized, SquircleGeometry.DefaultExponent);
        }

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = poweredCoordinates[x] + poweredCoordinates[y] <= 1d;
                var index = (y * size + x) * 4;
                pixels[index] = 255;
                pixels[index + 1] = 255;
                pixels[index + 2] = 255;
                pixels[index + 3] = inside ? (byte)255 : (byte)0;
            }
        }

        return CreateImageFromBgraPixels(size, pixels);
    }

    private static MagickImage CreateLiquidGlassOutline(int size, double thickness, LiquidGlassVariant variant)
    {
        var pixels = new byte[checked(size * size * 4)];
        var half = size / 2d;
        var safeThickness = Math.Max(0.5d, thickness);
        var normalizedCoordinates = new double[size];
        var poweredCoordinates = new double[size];

        for (var coordinate = 0; coordinate < size; coordinate++)
        {
            var normalized = (coordinate + 0.5d - half) / half;
            var absolute = Math.Abs(normalized);
            normalizedCoordinates[coordinate] = normalized;
            poweredCoordinates[coordinate] = Math.Pow(absolute, SquircleGeometry.DefaultExponent);
        }

        for (var y = 0; y < size; y++)
        {
            var normalizedY = normalizedCoordinates[y];
            for (var x = 0; x < size; x++)
            {
                var normalizedX = normalizedCoordinates[x];
                var field = poweredCoordinates[x] + poweredCoordinates[y];
                if (field > 1d || field < 1e-12) continue;

                var boundaryScale = Math.Pow(field, -1d / SquircleGeometry.DefaultExponent);
                var distance = (boundaryScale - 1d) *
                               Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY) * half;
                if (distance > safeThickness) continue;

                var position = Math.Clamp(distance / safeThickness, 0d, 1d);
                var body = Math.Sqrt(Math.Max(0d, 4d * position * (1d - position)));

                var outerWidth = Math.Max(0.8d, safeThickness * 0.11d);
                var outerRim = 1d - Math.Clamp(distance / outerWidth, 0d, 1d);
                outerRim *= outerRim;

                var innerCenter = safeThickness * 0.88d;
                var innerWidth = Math.Max(1d, safeThickness * 0.12d);
                var innerRim = 1d - Math.Clamp(Math.Abs(distance - innerCenter) / innerWidth, 0d, 1d);
                innerRim *= innerRim;

                var lightDirection = Math.Clamp(0.5d - (normalizedX + normalizedY) * 0.3d, 0d, 1d);
                var specular = outerRim * (0.28d + 0.72d * lightDirection) +
                               innerRim * (0.12d + 0.88d * lightDirection);
                var shadow = outerRim * (1d - lightDirection);

                double alphaValue;
                double colorMix;
                (double R, double G, double B) darkColor;
                (double R, double G, double B) lightColor;

                if (variant == LiquidGlassVariant.Light)
                {
                    alphaValue = 58d * body + 185d * specular + 88d * shadow;
                    colorMix = Math.Clamp(0.24d + 0.76d * lightDirection + 0.18d * innerRim, 0d, 1d);
                    darkColor = (24d, 31d, 41d);
                    lightColor = (248d, 253d, 255d);
                }
                else
                {
                    alphaValue = 100d * body + 175d * specular + 145d * shadow;
                    colorMix = Math.Clamp(0.06d + 0.72d * lightDirection * specular + 0.22d * lightDirection * body, 0d, 1d);
                    darkColor = (4d, 7d, 12d);
                    lightColor = (228d, 247d, 255d);
                }

                var red = darkColor.R + (lightColor.R - darkColor.R) * colorMix;
                var green = darkColor.G + (lightColor.G - darkColor.G) * colorMix;
                var blue = darkColor.B + (lightColor.B - darkColor.B) * colorMix;
                var alpha = (byte)Math.Clamp(Math.Round(alphaValue), 0d, 235d);
                var index = (y * size + x) * 4;
                pixels[index] = (byte)Math.Clamp(Math.Round(blue), 0d, 255d);
                pixels[index + 1] = (byte)Math.Clamp(Math.Round(green), 0d, 255d);
                pixels[index + 2] = (byte)Math.Clamp(Math.Round(red), 0d, 255d);
                pixels[index + 3] = alpha;
            }
        }

        return CreateImageFromBgraPixels(size, pixels);
    }

    private static MagickImage CreateImageFromBgraPixels(int size, byte[] pixels)
    {
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
