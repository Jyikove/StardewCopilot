using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace StardewCodexChat.UI;

/// <summary>Rasterize the system Chinese font at Retina density, instead of enlarging game pixels.</summary>
internal sealed class HighResolutionFont : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly SKTypeface typeface;
    private readonly SKTypeface boldTypeface;
    private readonly SKTypeface codeTypeface;
    private readonly SKPaint paint;
    private readonly Dictionary<(string Text, float Scale, bool Bold, bool Code), Texture2D> cache = new();
    private readonly Queue<(string Text, float Scale, bool Bold, bool Code)> order = new();
    private readonly List<Texture2D> retired = new();
    private int pixels;
    private float density = 2;
    private const float BaseSize = 20;
    private const int MaxCachePixels = 8 * 1024 * 1024;

    internal HighResolutionFont(GraphicsDevice device)
    {
        this.device = device;
        typeface = SKTypeface.FromFamilyName("PingFang SC", SKFontStyle.Normal)
            ?? SKTypeface.FromFile("/System/Library/Fonts/Hiragino Sans GB.ttc")
            ?? throw new InvalidOperationException("无法加载 macOS 中文字体。");
        boldTypeface = SKTypeface.FromFamilyName("PingFang SC", SKFontStyle.Bold) ?? typeface;
        codeTypeface = SKTypeface.FromFamilyName("Menlo", SKFontStyle.Normal) ?? typeface;
        paint = new SKPaint {
            Typeface = typeface, IsAntialias = true, SubpixelText = true,
            LcdRenderText = false, Color = SKColors.White
        };
    }

    internal void SetDensity(float value)
    {
        value = Math.Clamp(MathF.Ceiling(value), 2, 4);
        if (value == density) return;
        ClearTextures();
        density = value;
    }

    // All faces share the Chinese font's baseline, including Menlo code runs.
    private SKFontMetrics SelectFont(string text,float size,bool bold,bool code)
    {
        paint.TextSize=size;paint.Typeface=typeface;
        var metrics=paint.FontMetrics;
        paint.Typeface=code&&text.All(c=>c<128)?codeTypeface:bold?boldTypeface:typeface;
        return metrics;
    }
    internal Vector2 Measure(string text, float scale, bool bold = false, bool code = false)
    {
        var metrics=SelectFont(text,BaseSize*scale,bold,code);
        return new Vector2(paint.MeasureText(text),metrics.Descent-metrics.Ascent+metrics.Leading);
    }

    internal void BeginFrame()
    {
        foreach (Texture2D texture in retired) texture.Dispose();
        retired.Clear();
    }

    internal SKBitmap Rasterize(string text, float scale, bool bold = false, bool code = false)
    {
        var metrics=SelectFont(text,BaseSize*scale*density,bold,code);
        int width = Math.Max(1, (int)Math.Ceiling(paint.MeasureText(text) + 4 * density));
        int height = Math.Max(1, (int)Math.Ceiling(metrics.Descent - metrics.Ascent + 4 * density));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(text, 2 * density, 2 * density - metrics.Ascent, paint);
        canvas.Flush();
        return bitmap;
    }

    internal void Draw(SpriteBatch batch, string text, float x, float y, Color color, float scale, bool bold = false, bool code = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var key = (text, scale, bold, code);
        if (!cache.TryGetValue(key, out Texture2D? texture))
        {
            using var bitmap = Rasterize(text, scale, bold, code);
            int width = bitmap.Width, height = bitmap.Height;
            while (order.Count > 0 && (pixels + width * height > MaxCachePixels || cache.Count >= 256))
            {
                var oldest = order.Dequeue();
                Texture2D old = cache[oldest];
                pixels -= old.Width * old.Height;
                // SpriteBatch may still reference textures queued earlier in this frame.
                retired.Add(old); cache.Remove(oldest);
            }
            texture = new Texture2D(device, width, height, false, SurfaceFormat.Color);
            texture.SetData(bitmap.Bytes);
            cache.Add(key, texture); order.Enqueue(key); pixels += width * height;
        }
        batch.Draw(texture, new Vector2(x - 2, y - 2), null, color, 0, Vector2.Zero,
            1 / density, SpriteEffects.None, 0.9f);
    }

    private void ClearTextures()
    {
        foreach (Texture2D texture in cache.Values) texture.Dispose();
        BeginFrame();
        cache.Clear(); order.Clear(); pixels = 0;
    }

    public void Dispose() { ClearTextures(); paint.Dispose(); if(!ReferenceEquals(typeface,codeTypeface))codeTypeface.Dispose(); if (!ReferenceEquals(typeface, boldTypeface)) boldTypeface.Dispose(); typeface.Dispose(); }
}
