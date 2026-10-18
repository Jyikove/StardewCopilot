using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Menus;

namespace StardewCodexChat.UI;

/// <summary>A pixel-art chat medallion aligned with the vanilla 1.6.15 stamina HUD.</summary>
internal sealed class HudChatButton : IDisposable
{
    private Rectangle bounds;
    private long lastDraw = -1;
    private Texture2D? normal, hover;
    private const int Diameter = 48; // Vanilla bar: 12 source pixels at 4x scale.
    private const int SpriteSize = 24;

    private static bool Visible => Context.IsWorldReady && Game1.IsHudDrawn
        && Game1.activeClickableMenu == null && !Game1.eventUp && Game1.farmEvent == null
        && Game1.currentMinigame == null && !Game1.showingEndOfNightStuff;

    internal void Draw(SpriteBatch batch)
    {
        if (!Visible) { lastDraw = -1; return; }
        var device = Game1.graphics.GraphicsDevice;
        Rectangle safe = device.Viewport.GetTitleSafeArea();
        int x = safe.Right - 56;
        // Match the vanilla correction on outdoor maps narrower than the viewport.
        if (Game1.isOutdoorMapSmallerThanViewport())
            x = Math.Min(x, -Game1.viewport.X + Game1.currentLocation.map.Layers[0].LayerWidth * 64 - 48);
        int barTop = safe.Bottom - 240 - (int)((Game1.player.MaxStamina - 270) * 0.625f);
        int warningHeight = Game1.player.exhausted.Value ? 44 : 0;
        bounds = new(x, barTop - warningHeight - 8 - Diameter, Diameter, Diameter);
        if (bounds.Left < safe.Left || bounds.Top < safe.Top || bounds.Right > safe.Right) { lastDraw = -1; return; }
        lastDraw = Environment.TickCount64;
        if (normal == null || normal.IsDisposed || normal.GraphicsDevice != device)
        {
            Dispose();
            normal = CreateSprite(device, false);
            hover = CreateSprite(device, true);
        }
        bool highlighted = Contains(Game1.getMouseX(true), Game1.getMouseY(true));
        batch.Draw(highlighted ? hover! : normal, bounds, Color.White);
        if (highlighted)
            IClickableMenu.drawHoverText(batch, "打开 Stardew Copilot", Game1.smallFont);
    }

    internal bool HitTest(int x, int y) => Visible && lastDraw >= 0
        && Environment.TickCount64 - lastDraw < 500 && Contains(x, y);

    private bool Contains(int x, int y)
    {
        double dx = x - (bounds.X + Diameter / 2.0), dy = y - (bounds.Y + Diameter / 2.0);
        return dx * dx + dy * dy <= Diameter * Diameter / 4.0;
    }

    private static Texture2D CreateSprite(GraphicsDevice device, bool highlighted)
    {
        var pixels = new Color[SpriteSize * SpriteSize];
        Color outline = new(94, 57, 39);
        Color rim = highlighted ? new(242, 188, 94) : new(201, 142, 67);
        Color face = highlighted ? new(255, 237, 183) : new(246, 219, 157);
        Color shade = highlighted ? new(239, 203, 130) : new(224, 181, 109);
        Color ink = new(36, 91, 77);
        Color fill = highlighted ? new(76, 151, 116) : new(58, 126, 101);
        Color light = new(255, 243, 202);
        for (int y = 0; y < SpriteSize; y++)
        for (int x = 0; x < SpriteSize; x++)
        {
            double radius = (x - 11.5) * (x - 11.5) + (y - 11.5) * (y - 11.5);
            if (radius > 144) continue;
            Color color = radius > 115 ? outline : radius > 87 ? rim : y > 15 ? shade : face;
            if (radius is > 87 and < 115 && y < 9 && x < 14) color = light;
            pixels[y * SpriteSize + x] = color;
        }
        // A chamfered speech bubble, tail and three square dots. No smooth edges.
        for (int y = 7; y <= 15; y++)
        for (int x = 5; x <= 18; x++)
        {
            if ((y == 7 || y == 15) && (x == 5 || x == 18)) continue;
            pixels[y * SpriteSize + x] = x is 5 or 18 || y is 7 or 15 ? ink : fill;
        }
        pixels[16 * SpriteSize + 8] = ink;
        pixels[16 * SpriteSize + 9] = fill;
        pixels[17 * SpriteSize + 8] = ink;
        foreach (int x in new[] { 7, 11, 15 })
            for (int y = 10; y <= 11; y++)
                for (int offset = 0; offset < 2; offset++) pixels[y * SpriteSize + x + offset] = light;
        var sprite = new Texture2D(device, SpriteSize, SpriteSize);
        sprite.SetData(pixels);
        return sprite;
    }

    public void Dispose()
    {
        normal?.Dispose(); hover?.Dispose(); normal = null; hover = null;
    }
}
