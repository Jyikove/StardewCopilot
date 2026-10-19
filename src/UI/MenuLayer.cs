using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;

namespace StardewCodexChat.UI;

/// <summary>Use the game's child-menu stack: parent stays active, drawn and undisposed.</summary>
internal sealed class MenuLayer
{
    private IClickableMenu? root,parent;
    private bool open,ownsPause,previousPause;
    internal IClickableMenu? Current {get;private set;}
    internal bool IsOpen=>open;
    internal static IClickableMenu? Top(IClickableMenu? menu)
    {
        for(int i=0;menu?.GetChildMenu()!=null&&i<32;i++)menu=menu.GetChildMenu();
        return menu;
    }
    internal bool IsShowing(IClickableMenu? menu)=>open&&menu!=null&&ReferenceEquals(Current,menu)&&ReferenceEquals(Top(Game1.activeClickableMenu),menu);
    internal IClickableMenu Show(Func<IClickableMenu> create)
    {
        if(!open)
        {
            root=Game1.activeClickableMenu;parent=Top(root);open=true;
            if(!Context.IsMultiplayer){previousPause=Game1.isTimePaused;ownsPause=true;Game1.isTimePaused=true;}
        }
        // Restore the old keyboard subscriber before constructing its successor.
        if(Current is IDisposable disposable)disposable.Dispose();
        var next=create();Current=next;
        next.exitFunction+=()=>{if(ReferenceEquals(Current,next))Close();};
        if(parent!=null)parent.SetChildMenu(next);
        else Game1.activeClickableMenu=next;
        return next;
    }
    internal void Tick()
    {
        if(!open)return;
        if(!IsShowing(Current)){Close();return;}
        if(ownsPause)
        {
            if(!Context.IsMultiplayer)Game1.isTimePaused=true;
            else
            {
                if(Game1.isTimePaused)Game1.isTimePaused=previousPause;
                ownsPause=false;
            }
        }
    }
    internal void Resize(Microsoft.Xna.Framework.Rectangle before,Microsoft.Xna.Framework.Rectangle after)
    {
        // The native window handler resizes the root; forward only to our child.
        if(parent!=null&&IsShowing(Current))Current!.gameWindowSizeChanged(before,after);
    }
    internal void Close()
    {
        if(!open)return;
        var owned=Current;Current=null;open=false;
        try
        {
            bool returningToParent=ReferenceEquals(Game1.activeClickableMenu,root)
                &&(parent==null||parent.GetChildMenu()==null||ReferenceEquals(parent.GetChildMenu(),owned));
            var subscriber=Game1.keyboardDispatcher.Subscriber;
            if(owned is IDisposable disposable)disposable.Dispose();
            if(!returningToParent&&subscriber==null)Game1.keyboardDispatcher.Subscriber=null;
            if(parent!=null)
            {
                if(ReferenceEquals(parent.GetChildMenu(),owned))parent.SetChildMenu(null);
            }
            else if(owned!=null&&ReferenceEquals(Game1.activeClickableMenu,owned))Game1.activeClickableMenu=null;
        }
        finally
        {
            if(ownsPause&&Game1.isTimePaused)Game1.isTimePaused=previousPause;
            ownsPause=false;root=null;parent=null;
        }
    }
}
