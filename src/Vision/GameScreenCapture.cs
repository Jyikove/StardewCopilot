using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;
using StardewModdingAPI;
using StardewValley;
using StardewCodexChat.Core;

namespace StardewCodexChat.Vision;

/// <summary>Read the game's own framebuffer before our overlay. No desktop capture API or files.</summary>
internal sealed class GameScreenCapture : IScreenCapture
{
    private sealed record Request(CancellationToken Token, TaskCompletionSource<ScreenImageProcessor.Layers> Result);
    private readonly object gate = new();
    private Request? pending;
    private readonly int gameThread = Environment.CurrentManagedThreadId;

    public async Task<ScreenFrame> CaptureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new Request(token, new(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (gate)
        {
            if (pending != null && !pending.Token.IsCancellationRequested) throw new InvalidOperationException("正在截取游戏画面，请稍后重试。");
            pending = request;
        }
        try
        {
            var layers = await request.Result.Task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => { token.ThrowIfCancellationRequested(); return ScreenImageProcessor.Compose(layers); }, token).ConfigureAwait(false);
        }
        finally { lock (gate) if (ReferenceEquals(pending, request)) pending = null; }
    }

    internal void BeforeOverlay(SpriteBatch batch)
    {
        Request? request;
        lock (gate) { request = pending; pending = null; }
        if (request == null) return;
        if (request.Token.IsCancellationRequested) { request.Result.TrySetCanceled(request.Token); return; }
        if (Environment.CurrentManagedThreadId != gameThread) throw new InvalidOperationException("截图必须在游戏渲染线程读取。");
        if (!Context.IsWorldReady || Context.IsSplitScreen || !Game1.game1.IsActive)
        { request.Result.TrySetException(new InvalidOperationException("请在已载入存档且可见的非分屏游戏窗口中使用 /screen。")); return; }
        // Native DrawMenu uses this exact batch configuration. Flush the parent menu,
        // read it, then resume drawing our overlay in the same frame (no visible flash).
        batch.End();
        try
        {
            var device = Game1.graphics.GraphicsDevice;
            var presentation = device.PresentationParameters;
            int width = presentation.BackBufferWidth, height = presentation.BackBufferHeight;
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 32_000_000)
                throw new InvalidOperationException("游戏窗口过大，请缩小后重试 /screen。");
            ScreenImageProcessor.Pixels Read(RenderTarget2D? texture, float scale)
            {
                int w = texture?.Width ?? width, h = texture?.Height ?? height;
                if ((long)w * h > 32_000_000) throw new InvalidOperationException("游戏渲染画面过大。");
                byte[] bytes = new byte[checked(w * h * 4)];
                if (texture != null) texture.GetData(bytes);
                else ReadBackBuffer(device, bytes);
                return new(w, h, bytes, scale);
            }
            var targets = device.GetRenderTargets();
            var current = targets.Length > 0 ? targets[0].RenderTarget as RenderTarget2D : null;
            var ui = Game1.game1.uiScreen;
            ScreenImageProcessor.Pixels world;
            ScreenImageProcessor.Pixels? overlay = null;
            // Final render hooks may have returned to the world target already;
            // the completed UI layer still needs compositing, just as the game does.
            if (ui != null)
            {
                bool buffered = Game1.game1.ShouldDrawOnBuffer();
                world = Read(buffered ? Game1.game1.screen : null, buffered ? Game1.options.zoomLevel : 1);
                overlay = Read(ui, Game1.options.uiScale);
            }
            else world = Read(current, current != null && ReferenceEquals(current, Game1.game1.screen) ? Game1.options.zoomLevel : 1);
            request.Token.ThrowIfCancellationRequested();
            request.Result.TrySetResult(new(width, height, world, overlay, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) { request.Result.TrySetCanceled(request.Token); }
        catch { request.Result.TrySetException(new InvalidOperationException("无法读取当前游戏画面，请保持游戏窗口可见并重试 /screen。")); }
        finally { batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp); }
    }

    internal void Cancel()
    {
        lock (gate) { pending?.Result.TrySetCanceled(); pending = null; }
    }

    private static void ReadBackBuffer(GraphicsDevice device, byte[] bytes)
    {
        // This game's OpenGL GetBackBufferData reads the currently bound READ FBO,
        // not necessarily the window. UI mode has another FBO bound. Change only
        // the read binding, preserving the draw binding and MonoGame target cache.
        const uint readFramebuffer = 0x8CA8, readFramebufferBinding = 0x8CAA;
        glGetIntegerv(readFramebufferBinding, out int previous);
        try { glBindFramebuffer(readFramebuffer, 0); device.GetBackBufferData(bytes); }
        finally { glBindFramebuffer(readFramebuffer, (uint)previous); }
    }
    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(OpenGl)] private static extern void glGetIntegerv(uint name, out int value);
    [DllImport(OpenGl)] private static extern void glBindFramebuffer(uint target, uint framebuffer);
}
