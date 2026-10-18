using System.Runtime.InteropServices;
using SkiaSharp;
using StardewCodexChat.Core;

namespace StardewCodexChat.Vision;

/// <summary>CPU-only image processing. Call from a worker, never the game draw thread.</summary>
internal sealed class ScreenImageProcessor : IScreenImageProcessor
{
    internal sealed record Pixels(int Width, int Height, byte[] Rgba, float Scale);
    internal sealed record Layers(int Width, int Height, Pixels World, Pixels? Ui, DateTimeOffset CapturedAt);

    internal static ScreenFrame Compose(Layers layers)
    {
        using var result = new SKBitmap(layers.Width, layers.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Black);
        void Draw(Pixels pixels, bool alpha)
        {
            using var bitmap = Bitmap(pixels.Width, pixels.Height, pixels.Rgba, alpha);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low };
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, pixels.Width * pixels.Scale, pixels.Height * pixels.Scale), paint);
        }
        Draw(layers.World, false);
        if (layers.Ui != null) Draw(layers.Ui, true);
        byte[] bytes = new byte[checked(layers.Width * layers.Height * 4)];
        Marshal.Copy(result.GetPixels(), bytes, 0, bytes.Length);
        return new(layers.Width, layers.Height, bytes, layers.CapturedAt);
    }

    public ScreenImage Preview(ScreenFrame original)
    {
        original.Validate();
        using var bitmap = Bitmap(original.Width, original.Height, original.Rgba);
        return Encode(bitmap, 960, "整体预览，与原始截图相同比例");
    }

    public ScreenImage Crop(ScreenFrame original, ScreenRegion region)
    {
        original.Validate();
        if (region.X < 0 || region.Y < 0 || region.Width < 1 || region.Height < 1
            || (long)region.X + region.Width > original.Width || (long)region.Y + region.Height > original.Height)
            throw new ArgumentException("原图裁剪范围无效。");
        using var bitmap = Bitmap(original.Width, original.Height, original.Rgba);
        using var crop = new SKBitmap(region.Width, region.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(bitmap, new SKRect(region.X, region.Y, region.X + region.Width, region.Y + region.Height),
                new SKRect(0, 0, region.Width, region.Height));
        return Encode(crop, 2048, $"原图局部：左上({region.X},{region.Y})，范围{region.Width}×{region.Height}像素，已扩展边界");
    }

    private static SKBitmap Bitmap(int width, int height, byte[] rgba, bool alpha = false)
    {
        if (width < 1 || height < 1 || rgba.Length != (long)width * height * 4) throw new ArgumentException("截图像素数据无效。");
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return bitmap;
    }

    private static ScreenImage Encode(SKBitmap bitmap, int maxEdge, string label)
    {
        float scale = Math.Min(1, maxEdge / (float)Math.Max(bitmap.Width, bitmap.Height));
        using var resized = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Opaque), SKFilterQuality.Medium) : null;
        var source = resized ?? bitmap;
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(source.Width, source.Height, encoded.ToArray(), label);
    }
}
