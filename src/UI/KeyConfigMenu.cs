using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewCodexChat.Core;
using StardewCodexChat.Input;

namespace StardewCodexChat.UI;

/// <summary>Private local credential editor. Never forwards text to ChatSession or network services.</summary>
internal sealed class KeyConfigMenu : IClickableMenu, IDisposable
{
    private readonly FileSecretStore store;
    private readonly Action completed;
    private readonly HighResolutionFont font;
    private readonly SdlTextInput input;
    private readonly IKeyboardSubscriber? previousSubscriber;
    private readonly TextBuffer draft = new();
    private Task? work;
    private bool disposed;
    private string status = "粘贴 DeepSeek API Key，确认后加密保存在 Mod 文件夹。";
    private Rectangle panel, editor, confirm, cancel;
    private int firstVisible;
    private string composition = "";
    private long compositionAt;
    private bool CompositionGuard => composition.Length > 0 || Environment.TickCount64 - compositionAt < 200;

    internal KeyConfigMenu(FileSecretStore store, Action completed)
    {
        this.store = store; this.completed = completed;
        font = new HighResolutionFont(Game1.graphics.GraphicsDevice);
        input = new SdlTextInput();
        previousSubscriber = Game1.keyboardDispatcher.Subscriber;
        Game1.keyboardDispatcher.Subscriber = null; Game1.keyboardDispatcher.Discard();
        Layout();
    }
    private void Layout()
    {
        width = Math.Max(1, Math.Min(560, Game1.uiViewport.Width - 24));
        height = Math.Max(1, Math.Min(285, Game1.uiViewport.Height - 24));
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2; yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        panel = new(xPositionOnScreen, yPositionOnScreen, width, height);
        editor = new(panel.X + 22, panel.Y + 92, Math.Max(1, width - 44), 44);
        confirm = new(panel.Right - 122, panel.Bottom - 58, 100, 36);
        cancel = new(confirm.X - 112, confirm.Y, 100, 36);
        font.SetDensity(Game1.graphics.GraphicsDevice.PresentationParameters.BackBufferWidth / (float)Math.Max(1, Game1.uiViewport.Width));
        input.SetCandidateRect(editor);
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    public override void update(GameTime time)
    {
        if (disposed) return;
        if (work?.IsCompleted == true)
        {
            bool ok = work.IsCompletedSuccessfully;
            _ = work.Exception; work = null;
            if (ok) { draft.Clear(); Dispose(); completed(); return; }
            status = "保存失败，请检查文件夹权限并重试。";
        }
        while (input.TryRead(out var e))
        {
            if (!Game1.game1.IsActive || work != null) continue;
            if (e.Type == 0x302) { composition = e.Text; compositionAt = Environment.TickCount64; }
            else if (e.Type == 0x303)
            {
                if (composition.Length > 0 || e.Text.Any(c => c > 127)) compositionAt = Environment.TickCount64;
                draft.Insert(e.Text); composition = "";
            }
            else if (e.Type == 0x300 && !CompositionGuard)
            {
                bool cmd = (e.Modifiers & 0x0c00) != 0;
                if (e.Key == 27 && !e.Repeat) { exitThisMenu(); return; }
                if (e.Key == 8) draft.Backspace();
                if (e.Key == 127) draft.Delete();
                if (e.Key == 97 && cmd) draft.SelectAll();
                if (e.Key == 118 && cmd && !e.Repeat) draft.Insert(input.ClipboardText());
                if (e.Key == 1073741904) { if (cmd) draft.MoveTo(0); else draft.MoveLeft(); }
                if (e.Key == 1073741903) { if (cmd) draft.MoveTo(draft.Text.Length); else draft.MoveRight(); }
                if (e.Key is 13 or 1073741912 && !e.Repeat) Save();
            }
        }
    }
    private void Save()
    {
        if (work != null || CompositionGuard) return;
        try { string value = FileSecretStore.Validate(draft.Text); work = store.SaveKeyAsync(value); status = "正在加密保存…"; }
        catch (InvalidOperationException) { status = "Key 需为 10–512 个字符，不能包含空格或换行。"; }
    }
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (work != null) return;
        if (cancel.Contains(x,y)) { exitThisMenu(); return; }
        if (CompositionGuard) return;
        if (confirm.Contains(x,y)) { Save(); return; }
        if (editor.Contains(x,y))
        {
            float step = Math.Max(1, font.Measure("•", .88f).X);
            draft.MoveTo(firstVisible + (int)Math.Round((x - editor.X - 12) / step));
        }
    }
    public override void receiveKeyPress(Keys key) { }
    public override void receiveGamePadButton(Buttons button) { }
    public override bool readyToClose() => work == null;
    public override void draw(SpriteBatch b)
    {
        if (disposed) return;
        font.BeginFrame();
        void Fill(Rectangle r, Color c) => b.Draw(Game1.staminaRect, r, c);
        void Box(Rectangle r, Color c) { Fill(r, new Color(219,208,187)); Fill(new(r.X+1,r.Y+1,Math.Max(1,r.Width-2),Math.Max(1,r.Height-2)),c); }
        void Text(string s, float x, float y, float scale = .8f, bool bold = false) => font.Draw(b,s,x,y,new Color(72,61,49),scale,bold);
        Fill(new(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*.38f);
        Fill(new(panel.X+3,panel.Y+5,panel.Width,panel.Height),Color.Black*.17f);
        Box(panel,new Color(249,245,234));
        Text("配置 API Key",panel.X+22,panel.Y+19,1,true);
        Text("仅用于连接 DeepSeek",panel.X+22,panel.Y+55,.78f);
        Box(editor,new Color(255,252,245));
        float step = Math.Max(1,font.Measure("•",.88f).X);
        int capacity = Math.Max(1,(int)((editor.Width-26)/step));
        firstVisible = Math.Clamp(firstVisible,0,draft.Text.Length);
        if (draft.Cursor < firstVisible) firstVisible=draft.Cursor;
        if (draft.Cursor >= firstVisible+capacity) firstVisible=draft.Cursor-capacity+1;
        int count=Math.Min(capacity,draft.Text.Length-firstVisible);
        if (draft.AllSelected) Fill(new(editor.X+12,editor.Y+10,(int)(count*step),25),new Color(217,225,207));
        if (draft.Text.Length==0) Text("粘贴 API Key…",editor.X+12,editor.Y+11,.88f);
        else Text(new string('•',count),editor.X+12,editor.Y+11,.88f);
        if (work==null && Environment.TickCount64%1000<600) Fill(new(editor.X+12+(int)((draft.Cursor-firstVisible)*step),editor.Y+10,1,24),new Color(91,116,81));
        // Wrap feedback to the available width without ever rendering credential text.
        string line=""; float y=editor.Bottom+14;
        foreach(char c in status)
        {
            if(font.Measure(line+c,.73f).X>panel.Width-44) { Text(line,panel.X+22,y,.73f);line="";y+=21; }
            line+=c;
        }
        Text(line,panel.X+22,y,.73f);
        Box(cancel,new Color(242,237,224)); Box(confirm,new Color(231,235,218));
        string a="取消",z=work==null?"确认":"请稍候";
        Text(a,cancel.Center.X-font.Measure(a,.8f).X/2,cancel.Y+9);
        Text(z,confirm.Center.X-font.Measure(z,.8f).X/2,confirm.Y+9);
        drawMouse(b);
    }
    protected override void cleanupBeforeExit() => Dispose();
    public override void emergencyShutDown() => Dispose();
    public void Dispose()
    {
        if (disposed) return; disposed=true; draft.Clear(); composition="";
        // Observe background errors if the game closes while a local operation is pending.
        if(work!=null) _=work.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
        input.Dispose(); font.Dispose(); Game1.keyboardDispatcher.Discard();
        if(Game1.keyboardDispatcher.Subscriber==null && previousSubscriber!=null) Game1.keyboardDispatcher.Subscriber=previousSubscriber;
    }
}
