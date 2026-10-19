using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewCodexChat.Input;

namespace StardewCodexChat.UI;

/// <summary>Check local credentials before constructing either visible screen.</summary>
internal sealed class PendingMenuOpen
{
    private Task<bool>? check;
    private IClickableMenu? root, top;
    private string? save;
    private Keys trigger;
    internal bool IsPending => check != null;

    internal void Start(string saveKey, Keys key, Func<Task<string>> readKey)
    {
        if (IsPending) return;
        save = saveKey; trigger = key;
        root = Game1.activeClickableMenu; top = MenuLayer.Top(root);
        check = CheckKey(readKey);
    }

    private static async Task<bool> CheckKey(Func<Task<string>> readKey)
    {
        try { _ = await readKey().ConfigureAwait(false); return true; }
        catch { return false; }
    }

    // Called only from the game thread. Never open over a changed save or menu.
    internal bool TryComplete(string? saveKey, out bool configured)
    {
        configured = false;
        if (check == null) return false;
        if (save != saveKey || !ReferenceEquals(root, Game1.activeClickableMenu)
            || !ReferenceEquals(top, MenuLayer.Top(root))
            || !MenuOpenPolicy.CanOpen(trigger, allowOverMenu: trigger == Keys.Enter || StardewModdingAPI.Context.IsMultiplayer))
        { Cancel(); return false; }
        if (!check.IsCompleted) return false;
        configured = check.GetAwaiter().GetResult();
        Cancel();
        return true;
    }

    internal void Cancel()
    {
        // CheckKey observes errors itself; late completion cannot reopen a window.
        check = null; root = top = null; save = null;
    }
}
