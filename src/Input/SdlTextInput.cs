using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewCodexChat.Input;

internal readonly record struct TextEvent(uint Type, string Text, int Key, ushort Modifiers, bool Repeat);

/// <summary>Capture keyboard/text events for this menu before MonoGame and mod hotkeys see them.</summary>
internal sealed class SdlTextInput : IDisposable
{
    private const string Library = "StardewCodexChat.SDL";
    private readonly ConcurrentQueue<TextEvent> events = new();
    private readonly EventFilter watch;
    private readonly EventFilter? previousFilter;
    private readonly IntPtr previousFilterPointer;
    private readonly IntPtr previousFilterData;
    private readonly IntPtr filterPointer;
    private readonly ConcurrentDictionary<int, byte> capturedKeys = new();
    private static readonly List<SdlTextInput> ChainedFilters = new();
    private readonly uint windowId;
    private readonly bool wasActive;
    private readonly IntPtr cocoaWindow;
    private readonly nint oldWindowLevel;
    private volatile bool disposed;

    static SdlTextInput()
    {
        NativeLibrary.SetDllImportResolver(typeof(SdlTextInput).Assembly, (name, _, _) =>
            name == Library ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libSDL2-2.0.0.dylib")) : IntPtr.Zero);
    }

    internal SdlTextInput()
    {
        IntPtr handle = Game1.game1.Window.Handle;
        windowId = SDL_GetWindowID(handle);
        if (windowId == 0) throw new InvalidOperationException("无法连接游戏的 SDL 窗口。");
        wasActive = SDL_IsTextInputActive() != 0;
        SDL_GetEventFilter(out previousFilterPointer, out previousFilterData);
        if (previousFilterPointer != IntPtr.Zero)
            previousFilter = Marshal.GetDelegateForFunctionPointer<EventFilter>(previousFilterPointer);
        watch = Filter;
        filterPointer = Marshal.GetFunctionPointerForDelegate(watch);
        // macOS fullscreen SDL windows can cover WeType's candidate window.
        // Temporarily use normal NSWindow level; no Objective-C swizzling or global hooks.
        var wm = new WmInfo();
        SDL_GetVersion(ref wm);
        if (SDL_GetWindowWMInfo(handle, ref wm) != 0 && wm.Subsystem == 4 && wm.Window != IntPtr.Zero)
        {
            cocoaWindow = wm.Window;
            oldWindowLevel = objc_get(cocoaWindow, sel_registerName("level"));
            objc_set(cocoaWindow, sel_registerName("setLevel:"), 0);
        }
        SDL_SetEventFilter(filterPointer, IntPtr.Zero);
        SDL_StopTextInput(); // Discard a marked opening hotkey before focusing this editor.
        SDL_StartTextInput();
    }

    internal bool TryRead(out TextEvent value) => events.TryDequeue(out value);

    internal void SetCandidateRect(Rectangle uiRect)
    {
        // SDL expects window points, not Retina drawable pixels or UI-scaled coordinates.
        SDL_GetWindowSize(Game1.game1.Window.Handle, out int windowWidth, out int windowHeight);
        float sx = windowWidth / (float)Math.Max(1, Game1.uiViewport.Width);
        float sy = windowHeight / (float)Math.Max(1, Game1.uiViewport.Height);
        var rect = new SdlRect { X = (int)(uiRect.X * sx), Y = (int)(uiRect.Y * sy),
            W = Math.Max(1, (int)(uiRect.Width * sx)), H = Math.Max(1, (int)(uiRect.Height * sy)) };
        SDL_SetTextInputRect(ref rect);
    }

    internal string ClipboardText()
    {
        IntPtr text = SDL_GetClipboardText();
        try { return Marshal.PtrToStringUTF8(text) ?? ""; }
        finally { if (text != IntPtr.Zero) SDL_free(text); }
    }

    private int Filter(IntPtr userdata, IntPtr value)
    {
        // SDL may invoke this on another thread. Copy data immediately; touch no game/UI objects.
        try
        {
            int accepted = previousFilter?.Invoke(previousFilterData, value) ?? 1;
            if (accepted == 0 || disposed || (uint)Marshal.ReadInt32(value, 8) != windowId) return accepted;
            uint type = (uint)Marshal.ReadInt32(value);
            if (type is 0x302 or 0x303)
            {
                byte[] text = new byte[32];
                Marshal.Copy(IntPtr.Add(value, 12), text, 0, 32);
                int end = Array.IndexOf(text, (byte)0);
                events.Enqueue(new(type, Encoding.UTF8.GetString(text, 0, end < 0 ? 32 : end), 0, 0, false));
                return 0;
            }
            else if (type == 0x305) // SDL_TEXTEDITING_EXT: pointer is borrowed; never free it here.
            {
                int offset = IntPtr.Size == 8 ? 16 : 12;
                events.Enqueue(new(0x302, Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(value, offset)) ?? "", 0, 0, false));
                return 0;
            }
            else if (type == 0x300)
            {
                capturedKeys.TryAdd(Marshal.ReadInt32(value, 20), 0);
                events.Enqueue(new(type, "", Marshal.ReadInt32(value, 20), (ushort)Marshal.ReadInt16(value, 24),
                    Marshal.ReadByte(value, 13) != 0));
                return 0;
            }
            else if (type == 0x301 && capturedKeys.TryRemove(Marshal.ReadInt32(value, 20), out _))
                return 0;
            // Key-ups for keys already held before opening must reach MonoGame, preventing stuck keys.
        }
        catch { /* Managed exceptions must never unwind through the native event pump. */ }
        return 1;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SDL_GetEventFilter(out IntPtr current, out _);
        if (current == filterPointer)
            SDL_SetEventFilter(previousFilterPointer, previousFilterData);
        else
        {
            // A later filter may have chained this callback; keep its delegate alive and forward only.
            lock (ChainedFilters) ChainedFilters.Add(this);
        }
        // Flush marked text and reset the candidate anchor before handing input back to the game.
        SDL_StopTextInput();
        var defaultRect = new SdlRect { W = 1, H = 1 };
        SDL_SetTextInputRect(ref defaultRect);
        if (wasActive) SDL_StartTextInput();
        if (cocoaWindow != IntPtr.Zero) objc_set(cocoaWindow, sel_registerName("setLevel:"), oldWindowLevel);
        GC.KeepAlive(watch);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SdlRect { public int X, Y, W, H; }
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct WmInfo
    {
        [FieldOffset(0)] public byte Major;
        [FieldOffset(1)] public byte Minor;
        [FieldOffset(2)] public byte Patch;
        [FieldOffset(4)] public int Subsystem;
        [FieldOffset(8)] public IntPtr Window;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EventFilter(IntPtr userdata, IntPtr value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_GetVersion(ref WmInfo info);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_GetWindowWMInfo(IntPtr window, ref WmInfo info);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint SDL_GetWindowID(IntPtr window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_GetWindowSize(IntPtr window, out int width, out int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_GetEventFilter(out IntPtr filter, out IntPtr userdata);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_SetEventFilter(IntPtr filter, IntPtr userdata);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_StartTextInput();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_StopTextInput();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_IsTextInputActive();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_SetTextInputRect(ref SdlRect rect);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GetClipboardText();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_free(IntPtr memory);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint objc_get(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void objc_set(IntPtr receiver, IntPtr selector, nint level);
}
