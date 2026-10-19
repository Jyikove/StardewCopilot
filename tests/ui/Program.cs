using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using StardewCodexChat.UI;
using StardewValley;

Console.WriteLine("AES-GCM on game runtime: "+System.Security.Cryptography.AesGcm.IsSupported);
string todoDirectory = Path.Combine(AppContext.BaseDirectory,"todo-preview-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(todoDirectory);
foreach (var (w,h,name) in new[]{(1280,720,"chat"),(640,480,"compact"),(960,600,"ime"),(960,600,"todo"),(960,600,"citations"),(960,600,"help"),(640,480,"help-small"),(960,600,"commands"),(640,480,"commands-small"),(960,600,"thinking"),(640,480,"thinking-small")})
{
    Game1.uiViewport = new(0,0,w,h);
    using var bitmap = new SKBitmap(w*2,h*2);
    using var canvas = new SKCanvas(bitmap);
    canvas.Scale(2); canvas.Clear(new SKColor(112,139,95));
    var session = new StardewCodexChat.ChatSession();
    session.Display.Add(new("user","罗宾的斧子在哪里？"));
    session.Display.Add(new("assistant","斧子在煤矿森林，靠近下水道入口的东南角。\n从玛妮的牧场往南走，过桥后沿河向东找找。找到后带给罗宾就可以完成任务。"));
    session.Display.Add(new("user","今天下午去找还来得及吗？"));
    session.Display.Add(new("assistant","来得及。现在是 14:20，你可以先去捡斧子，再到木匠商店找罗宾。"));
    var todos = new StardewCodexChat.Core.TodoList(todoDirectory,name);
    if(name=="todo") {
        foreach(string text in new[]{"归还罗宾的斧子", "给农场的作物浇水", "下矿前准备食物和火把"}) {todos.Draft.Replace(text); todos.Add();}
        todos.Toggle(1);
    }
    if(name.StartsWith("thinking")){session.IsBusy=true;session.AssistantActivity="正在思考";}
    using var menu = new ChatMenu(session,new(),todos);
    void Tab(int i) {var rects=(Rectangle[])typeof(ChatMenu).GetField("tabs",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(menu)!;menu.receiveLeftClick(rects[i].Center.X,rects[i].Center.Y);}

    if(name.StartsWith("thinking"))
    {
        SpriteBatch.RenderedText.Clear();menu.draw(new SpriteBatch(canvas));
        if(!SpriteBatch.RenderedText.Contains("小助手正在思考")||!SpriteBatch.RenderedText.Contains("......"))throw new Exception("thinking title or six-dot animation missing");
        Console.WriteLine("PASS THINKING UI: title retains text and a separately rendered fixed-width six-dot indicator");
    }
    if(name=="ime") {
        session.Draft.Replace("那我应该先去");
        typeof(ChatMenu).GetField("composition",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(menu,"mei kuang sen lin");
    }
    if(name=="todo") Tab(1);
    if(name.StartsWith("help")) Tab(2);
    if(name=="citations")
    {
        session.Display.Clear();session.Display.Add(new("assistant","铁矿石可在矿井中获取[https://stardewvalleywiki.com/Iron_Ore]，其他来源可参考铜矿页面[https://stardewvalleywiki.com/Copper_Ore https://stardewvalleywiki.com/Iron_Ore]。"));
    }
    menu.update(new()); menu.draw(new SpriteBatch(canvas));
    if(name=="citations")
    {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var hits=(List<(Rectangle Bounds,string Url)>)typeof(ChatMenu).GetField("citationHits",flags)!.GetValue(menu)!;
        if(hits.Count!=3)throw new Exception("citation hit regions missing");Console.WriteLine("PASS CITATION UI: inline references have individual hover regions");
        Game1.MouseX=hits[0].Bounds.Center.X;Game1.MouseY=hits[0].Bounds.Center.Y;
        SpriteBatch.RenderedText.Clear();canvas.Clear(new SKColor(112,139,95));menu.draw(new SpriteBatch(canvas));
        if(!SpriteBatch.RenderedText.Contains("https://stardewvalleywiki.com/Iron_Ore"))throw new Exception("hover URL missing");Console.WriteLine("PASS CITATION UI: hovering a number draws its URL tooltip");
        Game1.MouseX=0;Game1.MouseY=0;
    }
    if(name=="chat") {
        void Assert(bool ok,string label) {if(!ok) throw new Exception(label);Console.WriteLine("PASS UI: "+label);}
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var navigation=(Rectangle[])typeof(ChatMenu).GetField("tabs",flags)!.GetValue(menu)!;
        Assert(navigation.Length==3 && navigation.Max(r=>r.Width)-navigation.Min(r=>r.Width)<=1,"three navigation tabs divide the window equally");
        Rectangle Field()=>(Rectangle)typeof(ChatMenu).GetField("editor",flags)!.GetValue(menu)!;
        void Key(int code,int modifiers=0)=>typeof(ChatMenu).GetMethod("HandleKey",flags)!.Invoke(menu,new object[]{new StardewCodexChat.Input.TextEvent(0x300,"",modifiers,code,false)});
        var messageRows=(System.Collections.IList)typeof(ChatMenu).GetField("layouts",flags)!.GetValue(menu)!;
        var chatArea=(Rectangle)typeof(ChatMenu).GetField("history",flags)!.GetValue(menu)!;
        Assert(messageRows.Cast<object>().All(r=>(int)r.GetType().GetProperty("Width")!.GetValue(r)! == chatArea.Width),"user and assistant bubbles share the full content width");
        session.Draft.Replace("今天去煤矿森林");
        menu.update(new());
        using var measure=new HighResolutionFont(new object());
        int clickX=Field().X+13+(int)Math.Round(measure.Measure("今天",.88f).X);
        menu.receiveLeftClick(clickX,Field().Y+12);
        Assert(session.Draft.Cursor==2,"mouse click positions the caret within Chinese text");
        session.Draft.Insert("先");
        Assert(session.Draft.Text=="今天先去煤矿森林","typing at the clicked caret preserves the suffix");
        Key(1073741904); Assert(session.Draft.Cursor==2,"left arrow moves the caret");
        Key(1073741903); Assert(session.Draft.Cursor==3,"right arrow moves the caret");
        Key(13,1); Assert(session.Draft.Text=="今天先\n去煤矿森林" && session.Sends==0,"Shift+Enter inserts a newline without sending");
        int compactHeight=44; menu.update(new());
        Assert(Field().Height>compactHeight,"input expands to display the newline");
        Key(1073741905); Assert(session.Draft.Cursor>4,"down arrow moves to the second line");
        Key(1073741906); Assert(session.Draft.Cursor<=3,"up arrow returns to the first line");
        Key(13); Assert(session.Sends==1 && session.SentText.Contains('\n'),"plain Enter sends the complete multiline question");
        session.Draft.Replace("一\n二\n三\n四\n五"); menu.update(new()); menu.draw(new SpriteBatch(canvas));
        Assert(Field().Height==3*((int)Math.Ceiling(measure.Measure("中文Ag",.88f).Y)+5)+18,"input expansion stops at three visible lines");
        Key(1073741898); menu.draw(new SpriteBatch(canvas));
        menu.receiveLeftClick(Field().X+13,Field().Y+12);
        Assert(session.Draft.Cursor==0,"scrolling to the beginning permits clicking the first line");
        Key(1073741901); menu.draw(new SpriteBatch(canvas));
        int rowHeight=(int)Math.Ceiling(measure.Measure("中文Ag",.88f).Y)+5;
        menu.receiveLeftClick(Field().Right-5,Field().Y+9+2*rowHeight+2);
        Assert(session.Draft.Cursor==session.Draft.Text.Length,"clicking the last visible row reaches the end after vertical scrolling");
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"pinyin");
        Key(13); Key(13,1); Key(1073741906); menu.receiveLeftClick(Field().X+13,Field().Y+12);
        Assert(session.Sends==1 && session.Draft.Cursor==session.Draft.Text.Length,"IME composition protects confirmation Enter, Shift+Enter, arrows and clicks");
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"");
        session.IsBusy=true; session.Draft.Replace("/clearchat"); menu.update(new());
        int beforeCommand=session.Sends;
        var sendCommand=(Rectangle)typeof(ChatMenu).GetField("send",flags)!.GetValue(menu)!;
        menu.receiveLeftClick(sendCommand.Center.X,sendCommand.Center.Y);
        Assert(session.Sends==beforeCommand+1 && session.SentText=="/clearchat","send button dispatches a slash command even during an answer");
        session.IsBusy=false;
        session.Draft.Replace("第一行问题\n第二行补充\n第三行说明"); menu.update(new());
        canvas.Clear(new SKColor(112,139,95)); menu.draw(new SpriteBatch(canvas));
    }
    if(name=="todo") {
        void Assert(bool ok,string text) {if(!ok) throw new Exception(text); Console.WriteLine("PASS TODO UI: "+text);}
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var area=(Rectangle)typeof(ChatMenu).GetField("todoArea",flags)!.GetValue(menu)!;
        menu.receiveLeftClick(area.X+20,area.Y+22);
        Assert(todos.Items[2].Completed,"checkbox marks the newest todo complete");
        menu.draw(new SpriteBatch(canvas));
        var sortedRows=(System.Collections.IList)typeof(ChatMenu).GetField("todoLayouts",flags)!.GetValue(menu)!;
        int firstIndex=(int)sortedRows[0]!.GetType().GetProperty("Index")!.GetValue(sortedRows[0])!;
        Assert(firstIndex==0,"completed todo moves below unfinished todos");
        var moving=sortedRows.Cast<object>().Single(r=>(int)r.GetType().GetProperty("Index")!.GetValue(r)! == 2);
        int destination=(int)moving.GetType().GetProperty("Top")!.GetValue(moving)!;
        float offset=(float)typeof(ChatMenu).GetMethod("TodoOffset",flags)!.Invoke(menu,new[]{moving})!;
        Assert(offset<destination,"completion begins a smooth movement from the old row position");
        typeof(ChatMenu).GetField("todoAnimationStartedAt",flags)!.SetValue(menu,Environment.TickCount64-1000);
        menu.draw(new SpriteBatch(canvas));
        var moved=sortedRows.Cast<object>().Single(r=>(int)r.GetType().GetProperty("Index")!.GetValue(r)! == 2);
        int movedTop=(int)moved.GetType().GetProperty("Top")!.GetValue(moved)!;
        menu.receiveLeftClick(area.X+20,area.Y+movedTop+22);
        Assert(!todos.Items[2].Completed,"moved checkbox restores the newest todo");
        menu.draw(new SpriteBatch(canvas));
        typeof(ChatMenu).GetField("todoAnimationStartedAt",flags)!.SetValue(menu,Environment.TickCount64-1000);
        menu.draw(new SpriteBatch(canvas));
        sortedRows=(System.Collections.IList)typeof(ChatMenu).GetField("todoLayouts",flags)!.GetValue(menu)!;
        Assert((int)sortedRows[0]!.GetType().GetProperty("Index")!.GetValue(sortedRows[0])! == 2,"restored newest todo returns to the unfinished top");
        session.Draft.Replace("聊天草稿"); todos.Draft.Replace("新的待办");
        typeof(ChatMenu).GetMethod("HandleKey",flags)!.Invoke(menu,new object[]{new StardewCodexChat.Input.TextEvent(0x300,"",0,13,false)});
        Assert(todos.Items.Count==4 && todos.Items[3].Text=="新的待办" && todos.Draft.Text=="","Enter creates a todo and clears only the todo draft");
        Assert(session.Sends==0 && session.Draft.Text=="聊天草稿","adding a todo does not send an AI request or change chat draft");
        todos.Draft.Replace("点击加号创建"); menu.update(new());
        var add=(Rectangle)typeof(ChatMenu).GetField("send",flags)!.GetValue(menu)!;
        menu.receiveLeftClick(add.Center.X,add.Center.Y);
        Assert(todos.Items.Count==5,"plus button creates a todo");
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"pinyin");
        menu.receiveLeftClick(area.X+20,area.Y+22);
        Assert(!todos.Items[^1].Completed,"checkbox does not interrupt IME composition");
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"");
        menu.update(new());
        var topEditor=(Rectangle)typeof(ChatMenu).GetField("editor",flags)!.GetValue(menu)!;
        var listArea=(Rectangle)typeof(ChatMenu).GetField("todoArea",flags)!.GetValue(menu)!;
        Assert(topEditor.Bottom<listArea.Y,"todo input is above the list");
        for(int i=0;i<30;i++) {todos.Draft.Replace("新增待办 "+i);todos.Add();}
        menu.draw(new SpriteBatch(canvas));
        var layouts=(System.Collections.IList)typeof(ChatMenu).GetField("todoLayouts",flags)!.GetValue(menu)!;
        Assert(layouts.Count==30 && todos.Items.Count==35,"only 30 newest todos are displayed while old records are retained");
        int newest=(int)layouts[0]!.GetType().GetProperty("Index")!.GetValue(layouts[0])!;
        int oldest=(int)layouts[29]!.GetType().GetProperty("Index")!.GetValue(layouts[29])!;
        Assert(newest==34 && oldest==5,"display order within unfinished items is newest first and oldest last");
        typeof(ChatMenu).GetField("todoScroll",flags)!.SetValue(menu,100);
        todos.Draft.Replace("新建后回到顶部");
        typeof(ChatMenu).GetMethod("HandleKey",flags)!.Invoke(menu,new object[]{new StardewCodexChat.Input.TextEvent(0x300,"",0,13,false)});
        Assert((int)typeof(ChatMenu).GetField("todoScroll",flags)!.GetValue(menu)! == 0,"adding returns to the top of the list");
        Tab(0);
        menu.update(new());
        var chatEditor=(Rectangle)typeof(ChatMenu).GetField("editor",flags)!.GetValue(menu)!;
        Assert(chatEditor.Y>topEditor.Y,"chat tab retains its bottom input");
        Tab(1);
        menu.draw(new SpriteBatch(canvas));
        typeof(ChatMenu).GetField("todoAnimationStartedAt",flags)!.SetValue(menu,Environment.TickCount64-1000);
        canvas.Clear(new SKColor(112,139,95)); menu.draw(new SpriteBatch(canvas));
    }
    if(name=="help") {
        void Assert(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS HELP UI: "+label);}
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var rows=(System.Collections.IList)typeof(ChatMenu).GetField("helpLines",flags)!.GetValue(menu)!;
        Assert(rows.Count>0 && rows.Cast<object>().Count(r=>(bool)r.GetType().GetProperty("Bold")!.GetValue(r)!)==5,"five help headings have a native bold style");
        Assert(!rows.Cast<object>().Any(r=>((string)r.GetType().GetProperty("Text")!.GetValue(r)!).Contains("**")),"help content contains no Markdown bold markers");
        var hits=(List<(Rectangle Bounds,string Url)>)typeof(ChatMenu).GetField("helpHits",flags)!.GetValue(menu)!;
        Assert(hits.Any(h=>h.Url=="#特殊指令"),"document section references become native clickable anchors");
        var anchor=hits.First(h=>h.Url=="#特殊指令");menu.receiveLeftClick(anchor.Bounds.Center.X,anchor.Bounds.Center.Y);
        Assert((int)typeof(ChatMenu).GetField("helpScroll",flags)!.GetValue(menu)!>0,"clicking a section reference scrolls to the command section");
        typeof(ChatMenu).GetField("helpScroll",flags)!.SetValue(menu,0);menu.draw(new SpriteBatch(canvas));
        string draft=session.Draft.Text;
        typeof(ChatMenu).GetMethod("HandleKey",flags)!.Invoke(menu,new object[]{new StardewCodexChat.Input.TextEvent(0x300,"",0,8,false)});
        Assert(session.Draft.Text==draft && session.Sends==0,"help page does not edit or submit the hidden chat draft");
        menu.receiveScrollWheelAction(-120);
        Assert((int)typeof(ChatMenu).GetField("helpScroll",flags)!.GetValue(menu)!>0,"help page scrolls independently");
        menu.receiveScrollWheelAction(120);
        Tab(0); Assert(session.Draft.Text==draft,"returning from help preserves the chat draft");
        Tab(2);
    }
    if(name.StartsWith("commands"))
    {
        void Assert(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS COMMAND UI: "+label);}
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        void Key(int code)=>typeof(ChatMenu).GetMethod("HandleKey",flags)!.Invoke(menu,new object[]{new StardewCodexChat.Input.TextEvent(0x300,"",0,code,false)});
        session.Draft.Replace("/");menu.update(new());SpriteBatch.RenderedText.Clear();menu.draw(new SpriteBatch(canvas));
        var popup=(Rectangle)typeof(ChatMenu).GetMethod("CommandPopupBounds",flags)!.Invoke(menu,null)!;
        int rowHeight=(int)typeof(ChatMenu).GetProperty("CommandPopupRow",flags)!.GetValue(menu)!;
        int visibleRows=(popup.Height-30)/rowHeight;
        Assert(StardewCodexChat.Core.CommandCompletion.Entries.Count(e=>SpriteBatch.RenderedText.Contains(e.Name))==Math.Min(6,visibleRows),"slash shows the available commands within the screen height");
        var editorBounds=(Rectangle)typeof(ChatMenu).GetField("editor",flags)!.GetValue(menu)!;
        var panelBounds=(Rectangle)typeof(ChatMenu).GetField("panel",flags)!.GetValue(menu)!;
        Assert(popup.Bottom<editorBounds.Top&&popup.Left>=panelBounds.Left&&popup.Right<=panelBounds.Right&&popup.Top>panelBounds.Top+93,"command popup fits above editor within the window");
        Key(1073741905);Key(13);
        Assert(session.Draft.Text=="/screen "&&session.Sends==0,"down and Enter complete screen without sending");
        session.Draft.Replace("/");menu.update(new());Key(1073741906);SpriteBatch.RenderedText.Clear();menu.draw(new SpriteBatch(canvas));
        Assert(SpriteBatch.RenderedText.Contains("/resetkey"),"selection scrolls to the last command in a short window");Key(13);
        Assert(session.Draft.Text=="/resetkey"&&session.Sends==0,"up wraps and completing a local action does not execute it");
        session.Draft.Replace("/");menu.update(new());menu.receiveLeftClick(popup.X+20,popup.Y+26+10);
        Assert(session.Draft.Text=="/search "&&session.Sends==0,"clicking a suggestion completes search without sending");
        session.Draft.Replace("/");menu.update(new());
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"pinyin");Key(1073741905);Key(13);
        Assert(session.Draft.Text=="/"&&session.Sends==0,"IME composition keeps candidate arrows and Enter out of command completion");
        typeof(ChatMenu).GetField("composition",flags)!.SetValue(menu,"");menu.update(new());
        canvas.Clear(new SKColor(112,139,95));menu.draw(new SpriteBatch(canvas));
    }
    using var data=bitmap.Encode(SKEncodedImageFormat.Png,100);
    using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"{name}.png")); data.SaveTo(file);
    Console.WriteLine($"Rendered {name}: {w}x{h}");
}

foreach(var (w,h,name) in new[]{(960,600,"key-config"),(640,480,"key-config-small")})
{
    Game1.uiViewport=new(0,0,w,h);
    using var bitmap=new SKBitmap(w*2,h*2);using var canvas=new SKCanvas(bitmap);canvas.Scale(2);canvas.Clear(new SKColor(112,139,95));
    var folder=Path.Combine(todoDirectory,name);Directory.CreateDirectory(folder);
    var store=new StardewCodexChat.Core.FileSecretStore(Path.Combine(folder,"secrets.json"));
    int opened=0;
    var menu=new KeyConfigMenu(store,()=>opened++);
    var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
    var draft=(StardewCodexChat.Core.TextBuffer)typeof(KeyConfigMenu).GetField("draft",flags)!.GetValue(menu)!;
    Rectangle Editor()=>(Rectangle)typeof(KeyConfigMenu).GetField("editor",flags)!.GetValue(menu)!;
    Rectangle Confirm()=>(Rectangle)typeof(KeyConfigMenu).GetField("confirm",flags)!.GetValue(menu)!;
    void Assert(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS KEY UI: "+label);}
    void Key(int code,int modifiers=0){StardewCodexChat.Input.SdlTextInput.Events.Enqueue(new(0x300,"",modifiers,code,false));menu.update(new());}
    menu.draw(new SpriteBatch(canvas));
    using(var data=bitmap.Encode(SKEncodedImageFormat.Png,100))using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,name+".png")))data.SaveTo(file);
    if(name=="key-config")
    {
        menu.receiveLeftClick(Confirm().Center.X,Confirm().Center.Y);
        Assert(!File.Exists(Path.Combine(folder,"secrets.json")),"empty confirmation does not save or open chat");
        StardewCodexChat.Input.SdlTextInput.Clipboard="sk-ui-test-credential-12345";
        Key(118,0x400);
        Assert(draft.Text=="sk-ui-test-credential-12345","Cmd+V pastes into the private credential buffer");
        Key(1073741904);Assert(draft.Cursor==draft.Text.Length-1,"left arrow moves the password caret");
        menu.receiveLeftClick(Editor().X+12,Editor().Y+12);Assert(draft.Cursor==0,"mouse click moves the password caret");
        Key(97,0x400);Assert(draft.AllSelected,"Cmd+A selects the credential for replacement");
        SpriteBatch.RenderedText.Clear();menu.draw(new SpriteBatch(canvas));
        Assert(!SpriteBatch.RenderedText.Any(t=>t.Contains("sk-ui-test")) && SpriteBatch.RenderedText.Any(t=>t.Contains('•')),"credential is rendered only as password dots");
        Key(13);
        for(int i=0;i<200 && opened==0;i++){Thread.Sleep(5);menu.update(new());}
        Console.WriteLine("Setup result: "+opened+"; "+typeof(KeyConfigMenu).GetField("status",flags)!.GetValue(menu));
        Assert(opened==1 && draft.Text=="" && store.GetKeyAsync(CancellationToken.None).GetAwaiter().GetResult()=="sk-ui-test-credential-12345","Enter saves encrypted credentials and opens chat once, clearing the private draft");
    }
    menu.Dispose();
    int beforeReset=opened;
    using(var check=new KeyConfigMenu(store,()=>opened++))
    {
        for(int i=0;i<3;i++)check.update(new());
        Assert(opened==beforeReset,"explicit reset remains in the editor even when a credential already exists");
    }
    string before=File.Exists(Path.Combine(folder,"secrets.json"))?File.ReadAllText(Path.Combine(folder,"secrets.json")):"";
    using(var cancelledMenu=new KeyConfigMenu(store,()=>opened++)) {}
    Assert((File.Exists(Path.Combine(folder,"secrets.json"))?File.ReadAllText(Path.Combine(folder,"secrets.json")):"")==before,"dismissing reconfiguration preserves the previous credential file");
    Console.WriteLine("Rendered "+name);
}
Directory.Delete(todoDirectory,true);

namespace Microsoft.Xna.Framework.Graphics {
 public class Texture2D {}
 public class SpriteBatch(SKCanvas canvas) {public static List<string> RenderedText=new();
  public void Draw(Texture2D t,Rectangle r,Color c) {using var p=new SKPaint{Color=new SKColor(c.R,c.G,c.B,c.A)};canvas.DrawRect(r.X,r.Y,r.Width,r.Height,p);}
  public void Text(string text,float x,float y,Color c,float scale,bool bold=false) {RenderedText.Add(text);using var p=StardewCodexChat.UI.HighResolutionFont.Paint(scale,bold);p.Color=new(c.R,c.G,c.B,c.A);canvas.DrawText(text,x,y-p.FontMetrics.Ascent,p);}
 }
}
namespace StardewValley {
 public interface IKeyboardSubscriber {}
 public class Dispatcher { public IKeyboardSubscriber? Subscriber;public void Discard(){} }
 public class Device {public Presentation PresentationParameters=new();}
 public class Presentation {public int BackBufferWidth=2560;}
 public class Graphics {public Device GraphicsDevice=new();}
 public class ActiveGame {public bool IsActive=true;}
 public class Location {public WeatherInfo GetWeather()=>new();}
 public class WeatherInfo {public string Weather="Sun";}
 public class Player {public Location currentLocation=new();public int Money=5976;}
 public static class Game1 {public static int MouseX,MouseY;public static int getMouseX(bool ui)=>MouseX;public static int getMouseY(bool ui)=>MouseY;public static Rectangle uiViewport;public static Graphics graphics=new();public static Dispatcher keyboardDispatcher=new();public static ActiveGame game1=new();public static Texture2D staminaRect=new();public static string currentSeason="spring"; public static int dayOfMonth=21,timeOfDay=1420;public static Player player=new();}
}
namespace StardewValley.Menus {
 public class IClickableMenu {
  public int width,height,xPositionOnScreen,yPositionOnScreen;
  public virtual bool readyToClose()=>true;public virtual void gameWindowSizeChanged(Rectangle a,Rectangle b){} public virtual void update(GameTime t){} public virtual void draw(SpriteBatch b){} public virtual void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys k){} public virtual void receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons b){} public virtual void receiveLeftClick(int x,int y,bool playSound=true){} public virtual void receiveScrollWheelAction(int d){} protected virtual void cleanupBeforeExit(){}public virtual void emergencyShutDown(){} protected void exitThisMenu(){} protected void drawMouse(SpriteBatch b){}
 }
}
namespace StardewModdingAPI {public static class Context {public static bool IsWorldReady=true;}}
namespace StardewCodexChat.Core {public interface ISecretStore {Task<string> GetKeyAsync(CancellationToken token);}public class ChatOptions {}public static class Conversation {public const int MaxInputLength=4000;}public record ChatMessage(string Role,string Text);}
namespace StardewCodexChat {
 public class Draft {public string Text="";public bool AllSelected;public void Insert(string t){}public void Backspace(){}public void SelectAll(){}}
 public class ChatSession {public Core.TextBuffer Draft=new();public List<Core.ChatMessage> Display=new();public bool IsBusy;public string AssistantActivity="正在待命";public int Revision;public int Sends;public string SentText="";public bool Send(Core.ChatOptions o){Sends++;SentText=Draft.Text;return true;}public void Reset(){}public void Cancel(){}}
}
namespace StardewCodexChat.Input {
 public record struct TextEvent(uint Type,string Text,int Modifiers,int Key,bool Repeat);
 public class SdlTextInput:IDisposable {public static Queue<TextEvent> Events=new();public static string Clipboard="";public void SetCandidateRect(Rectangle r){}public bool TryRead(out TextEvent e)=>Events.TryDequeue(out e);public string ClipboardText()=>Clipboard;public void Dispose(){}}
}
namespace StardewCodexChat.UI {
 internal class HighResolutionFont:IDisposable {
  public HighResolutionFont(object d){} public void SetDensity(float d){} public void BeginFrame(){}public void Dispose(){}
  internal static SKPaint Paint(float scale,bool bold=false)=>new(){Typeface=SKTypeface.FromFamilyName("PingFang SC",bold?SKFontStyle.Bold:SKFontStyle.Normal),TextSize=20*scale,IsAntialias=true,SubpixelText=true};
  public Vector2 Measure(string text,float scale,bool bold=false,bool code=false){using var p=Paint(scale,bold);return new(p.MeasureText(text),p.FontSpacing);}
  public void Draw(SpriteBatch b,string t,float x,float y,Color c,float s,bool bold=false,bool code=false)=>b.Text(t,x,y,c,s,bold);
 }
}
