using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewCodexChat.Input;
using StardewCodexChat.UI;

int passed=0;
void Check(bool ok,string title){if(!ok)throw new Exception(title);passed++;Console.WriteLine("PASS "+title);}
void Reset()
{
    Game1.activeClickableMenu=null;Game1.isTimePaused=false;Game1.IsMultiplayer=false;Game1.options=new();
    Game1.keyboardDispatcher.Subscriber=null;Game1.chatBox.Active=false;Game1.currentMinigame=null;
    Game1.farmEvent=null;Game1.eventUp=false;Game1.Fading=false;Game1.showingEndOfNightStuff=false;
    Context.IsWorldReady=true;Context.IsGameLaunched=true;
}
Reset();
var parent=new ShopMenu{HeldItems=37};Game1.activeClickableMenu=parent;
var originalSubscriber=new Focus{Selected=false};Game1.keyboardDispatcher.Subscriber=originalSubscriber;
var layer=new MenuLayer();var first=(Assistant)layer.Show(()=>new Assistant());
Check(ReferenceEquals(Game1.activeClickableMenu,parent)&&ReferenceEquals(parent.GetChildMenu(),first),"assistant is a real child; original active menu is never replaced");
Check(parent.Disposals==0&&parent.HeldItems==37&&Game1.isTimePaused,"opening preserves original items and explicitly pauses single-player time");
IClickableMenu? drawn=Game1.activeClickableMenu;while(drawn!=null){drawn.Draws++;drawn=drawn.GetChildMenu();}
MenuLayer.Top(Game1.activeClickableMenu)!.Updates++;
Check(parent.Draws==1&&first.Draws==1&&parent.Updates==0&&first.Updates==1,"native draw order keeps the parent behind the assistant while only the child updates");
var next=(Assistant)layer.Show(()=>new Assistant());
Check(first.Disposals==1&&parent.Disposals==0&&layer.IsShowing(next),"key-to-chat handoff disposes only the assistant screen");
Check(ReferenceEquals(next.PreviousSubscriber,originalSubscriber),"handoff restores original keyboard ownership before creating the next editor");
layer.Resize(new(0,0,800,600),new(0,0,640,480));
Check(next.Resizes==1&&parent.Resizes==0,"child gets its own window-resize event without reinitializing the parent");
next.Exit();
Check(ReferenceEquals(Game1.activeClickableMenu,parent)&&parent.GetChildMenu()==null&&parent.Disposals==0&&parent.HeldItems==37,"Esc removes only the child and retains exactly the same original menu and held items");
Check(!layer.IsOpen&&!Game1.isTimePaused&&ReferenceEquals(Game1.keyboardDispatcher.Subscriber,originalSubscriber),"closing restores pause and keyboard ownership");
layer.Close();Check(next.Disposals==1&&parent.Disposals==0,"repeated cleanup never disposes or closes the old menu");
var nested=new MapPage();parent.SetChildMenu(nested);
var nestedAssistant=(Assistant)layer.Show(()=>new Assistant());
Check(ReferenceEquals(parent.GetChildMenu(),nested)&&ReferenceEquals(nested.GetChildMenu(),nestedAssistant),"preexisting native child menus remain intact below the new assistant");
nestedAssistant.Exit();Check(ReferenceEquals(parent.GetChildMenu(),nested)&&nested.GetChildMenu()==null,"closing returns to the preexisting native child rather than its root");
parent.SetChildMenu(null);Game1.isTimePaused=true;
((Assistant)layer.Show(()=>new Assistant())).Exit();Check(Game1.isTimePaused,"preexisting pause owned by another feature is not cleared");
Game1.isTimePaused=false;layer.Show(()=>new Assistant());var replacement=new MapPage();Game1.activeClickableMenu=replacement;
Game1.keyboardDispatcher.Subscriber=null;layer.Tick();
Check(ReferenceEquals(Game1.activeClickableMenu,replacement)&&!layer.IsOpen&&!Game1.isTimePaused,"unexpected root changes close the orphaned assistant without restoring or overwriting menus");
Check(Game1.keyboardDispatcher.Subscriber==null,"orphan cleanup cannot restore a stale text subscriber from the old menu");
Reset();var bare=(Assistant)layer.Show(()=>new Assistant());bare.Exit();
Check(Game1.activeClickableMenu==null&&!Game1.isTimePaused,"opening from the world still returns to the world and resumes time");
Reset();Game1.IsMultiplayer=true;layer.Show(()=>new Assistant());layer.Close();
Check(!Game1.isTimePaused,"the layer never forces multiplayer time to pause");
Reset();layer.Show(()=>new Assistant());Game1.IsMultiplayer=true;layer.Tick();
Check(!Game1.isTimePaused,"a session becoming multiplayer releases the single-player pause lease");layer.Close();
Reset();parent=new ShopMenu();Game1.activeClickableMenu=parent;
try{layer.Show(()=>throw new Exception("construction failed"));}catch{layer.Close();}
Check(ReferenceEquals(Game1.activeClickableMenu,parent)&&parent.GetChildMenu()==null&&parent.Disposals==0&&!Game1.isTimePaused,"failed editor construction rolls back only the new layer");
foreach(IClickableMenu menu in new IClickableMenu[]{new ShopMenu(),new ItemGrabMenu(),new MapPage(),new QuestLog(),new DialogueBox(),new BobberBar(),new GameMenu{Page=new MapPage()}})
{
    Reset();Game1.activeClickableMenu=menu;
    Check(MenuOpenPolicy.CanOpen(Keys.Enter),"Enter can overlay an unbound native menu: "+menu.GetType().Name);
}
Reset();Game1.keyboardDispatcher.Subscriber=new Focus{Selected=true};
Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"selected text editors keep Enter for input and confirmation");
Reset();Game1.chatBox.Active=true;Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"active game chat keeps its Enter key");
Reset();Game1.options.actionButton=new[]{new InputButton(Keys.Enter)};Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"a player's explicit Enter action binding takes priority");
Check(MenuOpenPolicy.CanOpen(Keys.G),"a nonconflicting configured shortcut still works independently of Enter bindings");
Reset();Game1.activeClickableMenu=new GameMenu{Page=new OptionsPage{options=new(){new OptionsInputListener{listening=true}}}};
Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"rebinding a game key receives Enter instead of opening the assistant");
foreach(IClickableMenu menu in new IClickableMenu[]{new NamingMenu(),new NumberSelectionMenu(),new SaveGameMenu(),new ShippingMenu()})
{
    Reset();Game1.activeClickableMenu=menu;Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"text/confirmation/save transition retains its keys: "+menu.GetType().Name);
}
Reset();Game1.activeClickableMenu=new CustomKeyMenu();Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"unknown custom keyboard handlers retain priority");
Reset();Game1.activeClickableMenu=new InheritedShopMenu();Check(MenuOpenPolicy.CanOpen(Keys.Enter),"custom menus inheriting native key behavior remain supported");
Reset();Game1.IsMultiplayer=true;Game1.activeClickableMenu=new ShopMenu();Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"multiplayer retains the existing no-other-menu opening rule");
Game1.activeClickableMenu=null;Check(MenuOpenPolicy.CanOpen(Keys.Enter),"multiplayer can still open the assistant from normal gameplay");
Reset();Context.IsWorldReady=false;Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"title and load screens cannot open a save-bound assistant");
Reset();Game1.currentMinigame=new();Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"full-screen minigame controls keep keyboard ownership");
Reset();Game1.Fading=true;Check(!MenuOpenPolicy.CanOpen(Keys.Enter),"scene fades cannot be interrupted by a new layer");
Reset();
var opening=new PendingMenuOpen();
var ready=new TaskCompletionSource<string>();int checks=0;
opening.Start("save-a",Keys.Enter,()=>{checks++;return ready.Task;});
opening.Start("save-a",Keys.Enter,()=>{checks++;return Task.FromResult("unused");});
Check(opening.IsPending&&checks==1&&Game1.activeClickableMenu==null&&!Game1.isTimePaused,"checking credentials creates no visible menu, does not block, and ignores duplicate opens");
Check(!opening.TryComplete("save-a",out _)&&opening.IsPending,"unfinished credential reads leave the current screen untouched");
ready.SetResult("test-credential");
Check(opening.TryComplete("save-a",out bool configured)&&configured&&!opening.IsPending,"configured credentials select chat directly, with no key configuration screen");
Check(!opening.TryComplete("save-a",out _),"a successful open is delivered only once");
opening.Start("save-a",Keys.Enter,()=>Task.FromException<string>(new InvalidOperationException("missing or corrupt")));
Check(opening.TryComplete("save-a",out configured)&&!configured,"missing or unreadable credentials select configuration without an unobserved task error");
opening.Start("save-a",Keys.Enter,()=>throw new InvalidOperationException("read failed"));
Check(opening.TryComplete("save-a",out configured)&&!configured,"synchronous credential failures also select configuration");
ready=new();opening.Start("save-a",Keys.Enter,()=>ready.Task);opening.Cancel();
opening.Start("save-b",Keys.Enter,()=>Task.FromResult("new-credential"));ready.SetResult("old-credential");
Check(opening.TryComplete("save-b",out configured)&&configured,"late completion of a cancelled request cannot replace a newer opening");
opening.Start("save-a",Keys.Enter,()=>Task.FromResult("test-credential"));
Check(!opening.TryComplete("save-b",out _)&&!opening.IsPending,"switching saves discards pending opening");
parent=new ShopMenu{HeldItems=37};Game1.activeClickableMenu=parent;
ready=new();opening.Start("save-a",Keys.Enter,()=>ready.Task);ready.SetResult("test-credential");
Check(opening.TryComplete("save-a",out configured)&&configured&&parent.Disposals==0&&parent.HeldItems==37&&parent.GetChildMenu()==null,"credential checking over an existing menu never replaces, disposes, or overlays it with setup");
layer.Show(()=>new Assistant());layer.Close();
Check(ReferenceEquals(Game1.activeClickableMenu,parent)&&parent.Disposals==0&&!Game1.isTimePaused,"direct chat after checking retains native menu layering and pause restoration");
opening.Start("save-a",Keys.Enter,()=>Task.FromResult("test-credential"));Game1.activeClickableMenu=new MapPage();
Check(!opening.TryComplete("save-a",out _)&&!opening.IsPending,"changing the underlying menu cancels opening instead of stealing focus");
opening.Start("save-a",Keys.Enter,()=>Task.FromResult("test-credential"));Game1.activeClickableMenu.SetChildMenu(new QuestLog());
Check(!opening.TryComplete("save-a",out _),"changing the underlying native child also cancels opening");
Reset();opening.Start("save-a",Keys.Enter,()=>Task.FromResult("test-credential"));Game1.keyboardDispatcher.Subscriber=new Focus{Selected=true};
Check(!opening.TryComplete("save-a",out _),"a newly focused input retains keyboard ownership after the asynchronous check");
Reset();opening.Start("save-a",Keys.Enter,()=>Task.FromResult("test-credential"));Context.IsWorldReady=false;
Check(!opening.TryComplete("save-a",out _)&&!opening.IsPending,"leaving the save cannot display a late configuration or chat screen");
Console.WriteLine($"All {passed} menu-layer checks passed. No game save or network used.");

sealed class Focus:IKeyboardSubscriber{public bool Selected{get;set;}}
sealed class Assistant:IClickableMenu,IDisposable
{
    public IKeyboardSubscriber? PreviousSubscriber=Game1.keyboardDispatcher.Subscriber;
    public int Disposals;
    public Assistant(){Game1.keyboardDispatcher.Subscriber=null;}
    public void Dispose(){if(Disposals>0)return;Disposals++;if(Game1.keyboardDispatcher.Subscriber==null)Game1.keyboardDispatcher.Subscriber=PreviousSubscriber;}
}
sealed class CustomKeyMenu:IClickableMenu{public override void receiveKeyPress(Keys key){}}
sealed class InheritedShopMenu:ShopMenu{}

namespace StardewModdingAPI
{
    public static class Context{public static bool IsWorldReady=true,IsGameLaunched=true;public static bool IsMultiplayer=>Game1.IsMultiplayer;}
}
namespace StardewValley
{
    public interface IKeyboardSubscriber{bool Selected{get;set;}}
    public sealed class KeyboardDispatcher{public IKeyboardSubscriber? Subscriber;}
    public sealed class ChatBox{public bool Active;public bool isActive()=>Active;}
    public readonly record struct InputButton(Keys Key);
    public sealed class Options
    {
        public InputButton[] actionButton=Array.Empty<InputButton>(),menuButton=new[]{new InputButton(Keys.Escape)},chatButton=new[]{new InputButton(Keys.Enter)},tmpKeyToReplace=new[]{new InputButton(Keys.Enter)};
        public bool doesInputListContain(InputButton[] buttons,Keys key)=>buttons.Any(b=>b.Key==key);
    }
    public static class Game1
    {
        public static bool IsMultiplayer,isTimePaused,eventUp,showingEndOfNightStuff,Fading;
        public static object? currentMinigame,farmEvent;
        public static bool IsFading()=>Fading;
        public static Options options=new();public static KeyboardDispatcher keyboardDispatcher=new();public static ChatBox chatBox=new();
        private static IClickableMenu? active;
        public static IClickableMenu? activeClickableMenu{get=>active;set{if(!ReferenceEquals(active,value)&&active is IDisposable d)d.Dispose();active=value;}}
    }
}
namespace StardewValley.Menus
{
    public class IClickableMenu
    {
        private IClickableMenu? child,parent;
        public Action? exitFunction;public int Updates,Draws,Resizes;
        public IClickableMenu? GetChildMenu()=>child;
        public void SetChildMenu(IClickableMenu? value){child=value;if(value!=null)value.parent=this;}
        public virtual void receiveKeyPress(Keys key){}
        public virtual void gameWindowSizeChanged(Rectangle before,Rectangle after){Resizes++;}
        public void Exit(){if(this is IDisposable d)d.Dispose();if(ReferenceEquals(Game1.activeClickableMenu,this))Game1.activeClickableMenu=null;if(parent!=null){parent.SetChildMenu(null);parent=null;}var callback=exitFunction;exitFunction=null;callback?.Invoke();}
    }
    public class ShopMenu:IClickableMenu,IDisposable{public int HeldItems,Disposals;public void Dispose(){Disposals++;}}
    public sealed class ItemGrabMenu:IClickableMenu{}
    public sealed class MapPage:IClickableMenu{}
    public sealed class QuestLog:IClickableMenu{}
    public sealed class DialogueBox:IClickableMenu{}
    public sealed class BobberBar:IClickableMenu{}
    public sealed class NamingMenu:IClickableMenu{}
    public sealed class NumberSelectionMenu:IClickableMenu{}
    public sealed class SaveGameMenu:IClickableMenu{}
    public sealed class ShippingMenu:IClickableMenu{}
    public sealed class GameMenu:IClickableMenu{public IClickableMenu? Page;public IClickableMenu? GetCurrentPage()=>Page;}
    public class OptionsElement{}
    public sealed class OptionsInputListener:OptionsElement{public bool listening;}
    public sealed class OptionsPage:IClickableMenu{public List<OptionsElement> options=new();}
}
