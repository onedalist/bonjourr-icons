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

            if (request.AddLiquidGlassOutline)
            {
                var glassScale = workingSize / 128d;
                using var refraction = CreateLiquidGlassRefraction(
                    prepared,
                    checked((int)workingSize),
                    12d * glassScale,
                    5d * glassScale,
                    0.7d * glassScale,
                    request.LiquidGlassVariant);
                prepared.Composite(refraction, CompositeOperator.Over);

                using var outline = CreateLiquidGlassOutline(
                    checked((int)workingSize),
                    4d * glassScale,
                    request.LiquidGlassVariant);
                prepared.Composite(outline, CompositeOperator.Over);
            }

            using (var mask = CreateMask(checked((int)workingSize)))
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

    private static MagickImage CreateLiquidGlassRefraction(
        MagickImage source,
        int size,
        double opticalDepth,
        double maximumDisplacement,
        double blurRadius,
        LiquidGlassVariant variant)
    {
        using var rawSource = source.Clone();
        rawSource.Format = MagickFormat.Bgra;
        var sourcePixels = rawSource.ToByteArray();
        var outputPixels = new byte[checked(size * size * 4)];
        var half = size / 2d;
        var normalizedCoordinates = new double[size];
        var poweredCoordinates = new double[size];
        var signedGradients = new double[size];

        for (var coordinate = 0; coordinate < size; coordinate++)
        {
            var normalized = (coordinate + 0.5d - half) / half;
            var absolute = Math.Abs(normalized);
            normalizedCoordinates[coordinate] = normalized;
            poweredCoordinates[coordinate] = Math.Pow(absolute, SquircleGeometry.DefaultExponent);
            signedGradients[coordinate] = Math.Sign(normalized) *
                                          Math.Pow(absolute, SquircleGeometry.DefaultExponent - 1d);
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
                if (distance > opticalDepth) continue;

                var gradientX = signedGradients[x];
                var gradientY = signedGradients[y];
                var gradientLength = Math.Sqrt(gradientX * gradientX + gradientY * gradientY);
                if (gradientLength < 1e-12) continue;

                var normalX = gradientX / gradientLength;
                var normalY = gradientY / gradientLength;
                var position = Math.Clamp(distance / opticalDepth, 0d, 1d);
                var lens = 1d - position;
                lens = lens * lens * (3d - 2d * lens);
                var displacement = maximumDisplacement * lens;
                var sampleX = x - normalX * displacement;
                var sampleY = y - normalY * displacement;
                var tangentX = -normalY;
                var tangentY = normalX;
                var localBlur = blurRadius * (0.35d + 0.65d * lens);

                SampleBgra(sourcePixels, size, sampleX, sampleY, out var blue0, out var green0, out var red0, out var alpha0);
                SampleBgra(sourcePixels, size, sampleX + tangentX * localBlur, sampleY + tangentY * localBlur, out var blue1, out var green1, out var red1, out var alpha1);
                SampleBgra(sourcePixels, size, sampleX - tangentX * localBlur, sampleY - tangentY * localBlur, out var blue2, out var green2, out var red2, out var alpha2);

                var blue = blue0 * 0.5d + (blue1 + blue2) * 0.25d;
                var green = green0 * 0.5d + (green1 + green2) * 0.25d;
                var red = red0 * 0.5d + (red1 + red2) * 0.25d;
                var sourceAlpha = alpha0 * 0.5d + (alpha1 + alpha2) * 0.25d;

                if (variant == LiquidGlassVariant.Light)
                {
                    red = red * 1.035d + 6d;
                    green = green * 1.045d + 8d;
                    blue = blue * 1.06d + 11d;
                }
                else
                {
                    red = red * 0.82d + 5d;
                    green = green * 0.86d + 7d;
                    blue = blue * 0.91d + 10d;
                }

                var fade = 1d - position * position;
                var materialOpacity = variant == LiquidGlassVariant.Light ? 0.78d : 0.84d;
                var outputAlpha = sourceAlpha * materialOpacity * fade;
                var index = (y * size + x) * 4;
                outputPixels[index] = (byte)Math.Clamp(Math.Round(blue), 0d, 255d);
                outputPixels[index + 1] = (byte)Math.Clamp(Math.Round(green), 0d, 255d);
                outputPixels[index + 2] = (byte)Math.Clamp(Math.Round(red), 0d, 255d);
                outputPixels[index + 3] = (byte)Math.Clamp(Math.Round(outputAlpha), 0d, 255d);
            }
        }

        return CreateImageFromBgraPixels(size, outputPixels);
    }

    private static void SampleBgra(
        byte[] pixels,
        int size,
        double x,
        double y,
        out double blue,
        out double green,
        out double red,
        out double alpha)
    {
        x = Math.Clamp(x, 0d, size - 1.001d);
        y = Math.Clamp(y, 0d, size - 1.001d);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, size - 1);
        var y1 = Math.Min(y0 + 1, size - 1);
        var fractionX = x - x0;
        var fractionY = y - y0;
        var topLeft = (y0 * size + x0) * 4;
        var topRight = (y0 * size + x1) * 4;
        var bottomLeft = (y1 * size + x0) * 4;
        var bottomRight = (y1 * size + x1) * 4;

        blue = Bilinear(pixels[topLeft], pixels[topRight], pixels[bottomLeft], pixels[bottomRight], fractionX, fractionY);
        green = Bilinear(pixels[topLeft + 1], pixels[topRight + 1], pixels[bottomLeft + 1], pixels[bottomRight + 1], fractionX, fractionY);
        red = Bilinear(pixels[topLeft + 2], pixels[topRight + 2], pixels[bottomLeft + 2], pixels[bottomRight + 2], fractionX, fractionY);
        alpha = Bilinear(pixels[topLeft + 3], pixels[topRight + 3], pixels[bottomLeft + 3], pixels[bottomRight + 3], fractionX, fractionY);
    }

    private static double Bilinear(double topLeft, double topRight, double bottomLeft, double bottomRight, double fractionX, double fractionY)
    {
        var top = topLeft + (topRight - topLeft) * fractionX;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * fractionX;
        return top + (bottom - top) * fractionY;
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
