using System.Reflection;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewCodexChat.UI;

namespace StardewCodexChat.Input;

internal static class MenuOpenPolicy
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly FieldInfo[] Bindings=typeof(Options).GetFields(Fields)
        .Where(f=>f.FieldType==typeof(InputButton[])&&f.Name is not ("chatButton" or "tmpKeyToReplace")).ToArray();
    internal static bool CanOpen(Keys key,bool allowOverMenu=true)
    {
        if(!Context.IsWorldReady||!Context.IsGameLaunched||Context.IsSplitScreen||Game1.chatBox?.isActive()==true)return false;
        if(Game1.keyboardDispatcher.Subscriber?.Selected==true)return false;
        if(Game1.currentMinigame!=null||Game1.farmEvent!=null||Game1.showingEndOfNightStuff||Game1.IsFading())return false;
        if(Game1.eventUp&&Game1.activeClickableMenu==null)return false;
        if(key!=Keys.None&&Bindings.Any(f=>f.GetValue(Game1.options) is InputButton[] keys&&Game1.options.doesInputListContain(keys,key)))return false;
        // In co-op, Enter usually opens the game's player chat even before its editor is active.
        if(Context.IsMultiplayer&&key!=Keys.None&&Game1.options.doesInputListContain(Game1.options.chatButton,key))return false;
        var top=MenuLayer.Top(Game1.activeClickableMenu);
        if(top==null)return true;
        if(!allowOverMenu)return false;
        // These screens capture confirmation keys, arbitrary typed keys, or save transitions.
        foreach(var candidate in new[]{top,top is GameMenu game?game.GetCurrentPage():null})
        {
            if(candidate==null)continue;
            // The world continues in co-op; don't suspend an ongoing fishing interaction.
            if(Context.IsMultiplayer&&candidate is BobberBar)return false;
            if(candidate.GetType().Name is "NamingMenu" or "NumberSelectionMenu" or "TextEntryMenu" or "SaveGameMenu" or "ShippingMenu" or "LevelUpMenu" or "TitleMenu" or "CharacterCustomization" or "ReadyCheckDialog")return false;
            if(candidate is OptionsPage page&&typeof(OptionsPage).GetField("options",Fields)?.GetValue(page) is IEnumerable<OptionsElement> options
                &&options.OfType<OptionsInputListener>().Any(listener=>typeof(OptionsInputListener).GetField("listening",Fields)?.GetValue(listener) is true))return false;
            var handler=candidate.GetType().GetMethod("receiveKeyPress",new[]{typeof(Keys)})?.DeclaringType;
            // Unknown mod keyboard handlers get priority; inherited native handlers remain supported.
            if(handler!=null&&(handler.Assembly!=typeof(Game1).Assembly||handler.Namespace!="StardewValley.Menus"))return false;
        }
        return true;
    }
}
