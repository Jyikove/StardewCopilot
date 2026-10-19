using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;
using StardewCodexChat.Core;
using StardewCodexChat.Input;
using ChatCommands = StardewCodexChat.Core.ChatCommands;

namespace StardewCodexChat.UI;

internal sealed class ChatMenu : IClickableMenu, IDisposable
{
    private static readonly Color CitationBlue = new(100, 163, 205);
    private readonly List<(Rectangle Bounds,string Url)> citationHits = new();
    private readonly CommandCompletion commandCompletion = new();
    private static readonly Color Ink = new(72, 61, 49);
    private static readonly Color Muted = new(145, 133, 114);
    private static readonly Color Accent = new(91, 116, 81);
    private static readonly Color Paper = new(249, 245, 234);
    private static readonly Color Border = new(219, 208, 187);
    private readonly ChatSession session;
    private readonly TodoList todos;
    private TextBuffer Draft => selectedTab == 1 ? todos.Draft : session.Draft;
    private readonly List<TodoLayout> todoLayouts = new();
    private int todoRevision = -1, todoWidth, todoHeight, todoScroll;
    private Rectangle todoArea;
    private Dictionary<int, float> todoAnimationFrom = new();
    private long todoAnimationStartedAt;
    private int animatedTodoIndex = -1;
    private readonly ChatOptions options;
    private readonly HighResolutionFont font;
    private readonly SdlTextInput input;
    private readonly IKeyboardSubscriber? previousSubscriber;
    private readonly List<MessageLayout> layouts = new();
    private readonly Rectangle[] tabs = new Rectangle[3];
    private Rectangle panel, history, editor, send, close, footer, candidateAnchor;
    private int selectedTab;
    private readonly List<HelpLine> helpLines = new();
    private Rectangle helpArea;
    private int helpWidth, helpHeight, helpScroll;
    private int firstEditorRow;
    private float? preferredEditorX;
    private EditorDocument? editorDocument;
    private long caretMovedAt = Environment.TickCount64;
    private string composition = "";
    private long lastCompositionAt;
    private int revision = -1, contentHeight, scrollFromBottom;
    private bool disposed, hadFocus;
    private string[] gameBarValues = Array.Empty<string>();
    private long gameBarRefreshedAt;
    private const float TextScale = 0.88f;
    private int LineHeight => (int)Math.Ceiling(font.Measure("中文Ag", TextScale).Y) + 5;

    internal ChatMenu(ChatSession session, ChatOptions options, TodoList todos)
    {
        this.session = session;
        this.todos = todos;
        this.options = options;
        font = new HighResolutionFont(Game1.graphics.GraphicsDevice);
        input = new SdlTextInput();
        previousSubscriber = Game1.keyboardDispatcher.Subscriber;
        Game1.keyboardDispatcher.Subscriber = null;
        Game1.keyboardDispatcher.Discard();
        hadFocus = Game1.game1.IsActive;
        Layout();
        RefreshGameBar();
    }

    private void Layout()
    {
        width = Math.Max(1, Math.Min(760, Game1.uiViewport.Width - 24));
        height = Math.Max(1, Math.Min(650, Game1.uiViewport.Height - 24));
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        panel = new(xPositionOnScreen, yPositionOnScreen, width, height);
        font.SetDensity(Game1.graphics.GraphicsDevice.PresentationParameters.BackBufferWidth / (float)Math.Max(1, Game1.uiViewport.Width));
        int pad = width < 440 ? 14 : 22;
        close = new(panel.Right - pad - 28, panel.Y + 13, 28, 28);
        for (int i = 0; i < tabs.Length; i++)
        {
            int left = panel.X + 1 + (panel.Width - 2) * i / tabs.Length;
            int right = panel.X + 1 + (panel.Width - 2) * (i + 1) / tabs.Length;
            tabs[i] = new(left, panel.Y + 57, right - left, 36);
        }
        footer = new(panel.X + pad, panel.Bottom - 36, width - pad * 2, 24);
        editor = new(panel.X + pad, footer.Y - 54, Math.Max(1, width - pad * 2 - 52), 44);
        send = new(editor.Right + 8, editor.Y, 44, 44);
        history = new(panel.X + pad, panel.Y + 108, Math.Max(1, width - pad * 2), Math.Max(1, editor.Y - panel.Y - 124));
        CandidateRect();
        revision = -1;
    }

    private void CandidateRect() => SetCandidateAnchor(new Rectangle(editor.X + 13, editor.Y + 10, 1, LineHeight));
    private void SetCandidateAnchor(Rectangle rect)
    {
        if (rect == candidateAnchor) return;
        candidateAnchor = rect;
        input.SetCandidateRect(rect);
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

    public override void update(GameTime time)
    {
        if (disposed) return;
        if(Environment.TickCount64-gameBarRefreshedAt>=1000)RefreshGameBar();
        bool active = Game1.game1.IsActive;
        if (!active && hadFocus) composition = "";
        if (active && !hadFocus) { candidateAnchor = Rectangle.Empty; CandidateRect(); }
        hadFocus = active;
        while (input.TryRead(out TextEvent e))
        {
            if (!active) { composition = ""; continue; }
            if (e.Type == 0x300) HandleKey(e);
            else if (selectedTab < 2 && e.Type == 0x302)
            {
                composition = e.Text;
                lastCompositionAt = Environment.TickCount64;
            }
            else if (selectedTab < 2 && e.Type == 0x303)
            {
                Draft.Insert(e.Text);
                caretMovedAt = Environment.TickCount64;
                preferredEditorX = null;
                if (composition.Length > 0 || e.Text.Any(c => c > 127)) lastCompositionAt = Environment.TickCount64;
                composition = "";
            }
            if (disposed) return;
        }
        RefreshEditorLayout();
        RefreshCommandCompletion();
        if (session.Revision != revision) RebuildMessages();
    }

    private void RefreshCommandCompletion()=>commandCompletion.Update(session.Draft.Text,session.Draft.Cursor,selectedTab==0&&!CompositionGuard);

    private bool CompositionGuard => composition.Length > 0 || Environment.TickCount64 - lastCompositionAt < 200;
    private void HandleKey(TextEvent e)
    {
        bool command = (e.Modifiers & 0x0c00) != 0;
        if (CompositionGuard) return;
        RefreshCommandCompletion();
        if(commandCompletion.Visible)
        {
            if(e.Key==1073741906){commandCompletion.Move(-1);return;}
            if(e.Key==1073741905){commandCompletion.Move(1);return;}
            if(e.Key is 13 or 1073741912 && !e.Repeat && (e.Modifiers&0x0003)==0){CompleteCommand();return;}
            if(e.Key==27&&!e.Repeat){commandCompletion.Dismiss();return;}
        }
        if (e.Key == 27 && !e.Repeat) { exitThisMenu(); return; }
        if (selectedTab == 2) return;
        caretMovedAt = Environment.TickCount64;
        if (e.Key is not (1073741906 or 1073741905)) preferredEditorX = null;
        if (e.Key == 8) Draft.Backspace();
        if (e.Key == 127) Draft.Delete(); // Forward Delete / Fn+Delete.
        if (e.Key == 1073741904) { if (command) Draft.MoveTo(0); else Draft.MoveLeft(); }
        if (e.Key == 1073741903) { if (command) Draft.MoveTo(Draft.Text.Length); else Draft.MoveRight(); }
        if (e.Key == 1073741906) MoveEditorVertical(-1);
        if (e.Key == 1073741905) MoveEditorVertical(1);
        if (e.Key == 1073741898) Draft.MoveTo(0);
        if (e.Key == 1073741901) Draft.MoveTo(Draft.Text.Length);
        if (e.Key == 97 && command) Draft.SelectAll();
        if (e.Key == 118 && command && !e.Repeat) Draft.Insert(input.ClipboardText());
        if (e.Key is 13 or 1073741912 && !e.Repeat)
        {
            if ((e.Modifiers & 0x0003) != 0) Draft.Insert("\n"); // KMOD_SHIFT.
            else Send();
        }
        // CompositionGuard above reserves candidate-confirmation Enter for the IME.
    }
    public override void receiveKeyPress(Keys key) { }
    public override void receiveGamePadButton(Buttons button) { }
    private void Send()
    {
        if (CompositionGuard || selectedTab == 2) return;
        RefreshCommandCompletion();
        if(commandCompletion.Visible){CompleteCommand();return;}
        if (selectedTab == 1)
        {
            if (todos.Add()) { RebuildTodos(); todoScroll = 0; }
        }
        else if (session.Send(options)) scrollFromBottom = 0;
    }
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (close.Contains(x, y)) { exitThisMenu(); return; }
        RefreshCommandCompletion();
        if(commandCompletion.Visible)
        {
            var bounds=CommandPopupBounds();
            if(bounds.Contains(x,y))
            {
                int row=(y-bounds.Y-CommandPopupHeader)/CommandPopupRow;
                int index=CommandPopupFirst(bounds)+row;
                if(y>=bounds.Y+CommandPopupHeader&&index<CommandCompletion.Entries.Count)CompleteCommand(index);
                return;
            }
        }
        for (int i = 0; i < tabs.Length; i++)
            if (tabs[i].Contains(x, y) && !CompositionGuard)
            {
                selectedTab = i; composition = ""; firstEditorRow = 0; preferredEditorX = null;
                RefreshEditorLayout(); return;
            }
        if (selectedTab == 2) return;
        if (selectedTab == 1 && !CompositionGuard)
        {
            RebuildTodos();
            foreach (var row in todoLayouts.AsEnumerable().Reverse())
            {
                var box = new Rectangle(todoArea.X + 10, todoArea.Y + (int)TodoOffset(row) - todoScroll + 12, 22, 22);
                // Clipped checkboxes cannot be clicked through the header or input field.
                if (todoArea.Contains(x, y) && box.Top >= todoArea.Top && box.Bottom <= todoArea.Bottom && box.Contains(x, y))
                { if (todos.Toggle(row.Index)) animatedTodoIndex = row.Index; return; }
            }
        }
        if (editor.Contains(x, y) && !CompositionGuard)
        {
            RefreshEditorLayout();
            var doc = EditorLines();
            FollowEditorCaret(doc);
            int row = Math.Clamp(firstEditorRow + (y - editor.Y - 9) / LineHeight, firstEditorRow,
                Math.Min(doc.Lines.Count - 1, firstEditorRow + EditorCapacity - 1));
            Draft.MoveTo(HitEditorLine(doc.Lines[row], x - editor.X - 13));
            preferredEditorX = null;
            caretMovedAt = Environment.TickCount64;
            return;
        }
        if (send.Contains(x, y))
        {
            if (selectedTab == 0 && session.IsBusy && ChatCommands.Parse(Draft.Text).Kind == ChatCommandKind.None) session.Cancel();
            else Send();
        }
    }
    public override void receiveScrollWheelAction(int direction)
    {
        RefreshCommandCompletion();
        if(commandCompletion.Visible){commandCompletion.Move(-Math.Sign(direction));return;}
        if (selectedTab == 2)
        {
            RebuildHelp();
            helpScroll = Math.Clamp(helpScroll - Math.Sign(direction) * LineHeight * 3, 0, Math.Max(0, helpHeight - helpArea.Height));
        }
        if (selectedTab == 1)
        {
            RebuildTodos();
            todoScroll = Math.Clamp(todoScroll - Math.Sign(direction) * LineHeight * 3, 0, Math.Max(0, todoHeight - todoArea.Height));
        }
        if (selectedTab == 0) scrollFromBottom = Math.Clamp(scrollFromBottom + Math.Sign(direction) * LineHeight * 3,
            0, Math.Max(0, contentHeight - history.Height));
    }

    private void RebuildMessages()
    {
        // Preserve the visible position when streaming adds text while reading older messages.
        int oldHeight = contentHeight;
        layouts.Clear(); contentHeight = 0;
        foreach (var message in session.Display)
        {
            int maxWidth = Math.Max(1, history.Width - 28);
            List<RichLine> lines = WrapMessage(message.Text, message.Role == "assistant", maxWidth);
            int bubbleWidth = history.Width;
            int h = lines.Count * LineHeight + 39;
            layouts.Add(new(message.Role, lines, contentHeight, h, bubbleWidth));
            contentHeight += h + 16;
        }
        if (layouts.Count > 0) contentHeight -= 16;
        if (scrollFromBottom > 0) scrollFromBottom += contentHeight - oldHeight;
        revision = session.Revision;
        scrollFromBottom = Math.Clamp(scrollFromBottom, 0, Math.Max(0, contentHeight - history.Height));
    }

    public override void draw(SpriteBatch b)
    {
        if (disposed) return;
        BeforeDraw?.Invoke(b);
        font.BeginFrame();
        RefreshEditorLayout();
        if (session.Revision != revision) RebuildMessages();
        Fill(b, new(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * .38f);
        Card(b, new(panel.X + 3, panel.Y + 6, panel.Width, panel.Height), Color.Black * .17f, Color.Black * .03f);
        Card(b, panel, Paper, Border);
        Icon(b, "star", panel.X + 22, panel.Y + 22, Accent);
        string activity=session.AssistantActivity;
        bool thinking=activity is "正在思考" or "正在分析画面";
        float dotsWidth=thinking?font.Measure("......",.94f).X+7:0;
        string title=Fit("小助手"+activity,Math.Max(1,panel.Width-106-(int)Math.Ceiling(dotsWidth)),.94f);
        Text(b,title,panel.X+48,panel.Y+17,Ink,.94f);
        if(thinking)
        {
            float dotsX=panel.X+48+font.Measure(title,.94f).X+6;
            Text(b,"......",dotsX,panel.Y+17,Muted*.22f,.94f);
            int count=1+(int)(Environment.TickCount64/320%6);
            Text(b,new string('.',count),dotsX,panel.Y+17,Accent,.94f);
        }
        Icon(b, "close", close.X + 7, close.Y + 7, Muted);
        Fill(b, new(panel.X + 1, panel.Y + 50, panel.Width - 2, 1), Border * .65f);
        string[] labels = { "对话", "待办", "帮助" };
        string[] tabIcons = { "chat", "todo", "help" };
        for (int i = 0; i < tabs.Length; i++)
        {
            Rectangle r = tabs[i]; bool active = selectedTab == i;
            if (active) Card(b, r, new Color(235, 235, 218), new Color(235, 235, 218));
            Vector2 labelSize = font.Measure(labels[i], .82f);
            const int iconSize = 18, iconGap = 8;
            int groupX = (int)Math.Round(r.Center.X - (iconSize + iconGap + labelSize.X) / 2);
            Icon(b, tabIcons[i], groupX, r.Center.Y - iconSize / 2, active ? Accent : Muted);
            Text(b, labels[i], groupX + iconSize + iconGap, r.Center.Y - labelSize.Y / 2, active ? Ink : Muted, .82f);
            if (i < tabs.Length - 1) Fill(b, new(r.Right, r.Y + 9, 1, r.Height - 18), Border * .55f);
            if (active) Fill(b, new(r.X + 10, r.Bottom - 2, r.Width - 20, 2), Accent);
        }
        if (selectedTab == 0)
        {
            DrawMessages(b);
            DrawEditor(b);
        }
        else if (selectedTab == 1)
        {
            DrawTodos(b);
            DrawEditor(b);
        }
        else DrawHelp(b);
        DrawGameBar(b);
        RefreshCommandCompletion();
        if(commandCompletion.Visible)DrawCommandPopup(b);
        else DrawCitationTooltip(b);
        drawMouse(b);
    }

    internal Action<SpriteBatch>? BeforeDraw { get; init; }

    private void RebuildHelp()
    {
        helpArea = new(history.X + 2, panel.Y + 108, Math.Max(1, history.Width - 12), Math.Max(1, footer.Y - panel.Y - 122));
        if (helpWidth != helpArea.Width)
        {
            helpLines.Clear(); helpHeight = 0;
            foreach (var section in HelpContent.Sections)
            {
                float headingScale = helpLines.Count == 0 ? 1.02f : .92f;
                foreach (string line in Wrap(section.Heading, helpArea.Width, headingScale, true))
                    AddLine(line, headingScale, true);
                helpHeight += 7;
                foreach (string paragraph in section.Body.Split('\n'))
                {
                    foreach (string line in Wrap(paragraph, helpArea.Width, .84f)) AddLine(line, .84f, false);
                    helpHeight += 5;
                }
                helpHeight += 17;
            }
            helpWidth = helpArea.Width;
        }
        helpScroll = Math.Clamp(helpScroll, 0, Math.Max(0, helpHeight - helpArea.Height));
        void AddLine(string text, float scale, bool bold)
        {
            int lineHeight = (int)Math.Ceiling(font.Measure("中文Ag", scale, bold).Y) + 5;
            helpLines.Add(new(text, helpHeight, lineHeight, scale, bold));
            helpHeight += lineHeight;
        }
    }
    private void DrawHelp(SpriteBatch b)
    {
        RebuildHelp();
        foreach (var line in helpLines)
        {
            int y = helpArea.Y + line.Top - helpScroll;
            if (y < helpArea.Top || y + line.Height > helpArea.Bottom) continue;
            Text(b, line.Text, helpArea.X, y, Ink, line.Scale, line.Bold);
        }
        if (helpHeight > helpArea.Height)
        {
            int thumb = Math.Min(helpArea.Height, Math.Max(22, helpArea.Height * helpArea.Height / helpHeight));
            int y = helpArea.Y + (helpArea.Height - thumb) * helpScroll / (helpHeight - helpArea.Height);
            Fill(b, new(helpArea.Right + 6, y, 3, thumb), Accent * .4f);
        }
    }
    private sealed record HelpLine(string Text, int Top, int Height, float Scale, bool Bold);

    private void DrawMessages(SpriteBatch b)
    {
        if (layouts.Count == 0)
        {
            int cy = history.Center.Y;
            Icon(b, "sprout", panel.Center.X - 8, cy - 55, Accent);
            CenterText(b, "今天，想问些什么？", cy - 15, Ink, 1f);
        }
        citationHits.Clear();
        int scrollTop = Math.Max(0, contentHeight - history.Height) - scrollFromBottom;
        foreach (MessageLayout layout in layouts)
        {
            int top = history.Y + layout.Top - scrollTop;
            bool user = layout.Role == "user", notice = layout.Role == "system";
            int x = history.X;
            var bubble = new Rectangle(x, top, layout.Width, layout.Height);
            Color background = user ? new(229, 235, 216) : notice ? new(243, 232, 211) : new(255, 252, 245);
            Card(b, bubble, background, user ? new(205, 217, 189) : Border * .75f, history);
            if (top + 7 >= history.Y && top + 28 <= history.Bottom)
                Text(b, user ? "你" : notice ? "提示" : "小助手", x + 14, top + 7, user ? Accent : Muted, .62f);
            for (int i = 0; i < layout.Lines.Count; i++)
            {
                int y = top + 29 + i * LineHeight;
                if (y >= history.Y && y + LineHeight <= history.Bottom)
                {
                    float runX=x+14;
                    foreach(var run in layout.Lines[i].Runs)
                    {
                        Text(b,run.Text,runX,y,run.Url==null?Ink:CitationBlue);
                        float w=font.Measure(run.Text,TextScale).X;
                        if(run.Url!=null)citationHits.Add((new Rectangle((int)runX,y,Math.Max(1,(int)Math.Ceiling(w)),LineHeight),run.Url));
                        runX+=w;
                    }
                }
            }
        }
        if (contentHeight > history.Height)
        {
            int thumbHeight = Math.Min(history.Height, Math.Max(22, history.Height * history.Height / contentHeight));
            int thumbY = history.Y + (int)((history.Height - thumbHeight) * (1 - scrollFromBottom / (float)(contentHeight - history.Height)));
            Fill(b, new(history.Right + 5, thumbY, 3, thumbHeight), Accent * .4f);
        }
    }

    private List<RichLine> WrapMessage(string text,bool citations,int maxWidth)
    {
        var parts=citations?CitationMarkup.Parse(text):new[]{new CitationPart(text)};
        var lines=new List<RichLine>();var runs=new List<CitationPart>();float used=0;
        void Line(){lines.Add(new(runs));runs=new();used=0;}
        void Add(string value,string? url)
        {
            float w=font.Measure(value,TextScale).X;
            if(used>0 && used+w>maxWidth)Line();
            if(runs.Count>0 && runs[^1].Url==url)runs[^1]=new(runs[^1].Text+value,url);
            else runs.Add(new(value,url));
            used+=w;
        }
        foreach(var part in parts)
        {
            if(part.Url!=null){Add(part.Text,part.Url);continue;}
            var elements=StringInfo.GetTextElementEnumerator(part.Text.Replace("\r",""));
            while(elements.MoveNext())
            {
                string element=elements.GetTextElement();if(element=="\n")Line();else Add(element,null);
            }
        }
        Line();return lines;
    }

    private void DrawCitationTooltip(SpriteBatch b)
    {
        if(selectedTab!=0)return;
        int mx=Game1.getMouseX(true),my=Game1.getMouseY(true);
        var hit=citationHits.FirstOrDefault(h=>h.Bounds.Contains(mx,my));
        if(hit.Url==null)return;
        int maxWidth=Math.Max(1,Math.Min(600,Game1.uiViewport.Width-28));
        var lines=Wrap(hit.Url,Math.Max(1,maxWidth-20),.72f);
        int w=Math.Min(maxWidth,Math.Max(30,(int)lines.Max(line=>font.Measure(line,.72f).X)+20));
        int lineHeight=(int)Math.Ceiling(font.Measure("Ag",.72f).Y)+3;
        int maxLines=Math.Max(1,(Game1.uiViewport.Height-32)/lineHeight);
        if(lines.Count>maxLines){lines=lines.Take(maxLines).ToList();lines[^1]=Fit(lines[^1]+"…",maxWidth-20,.72f);}
        int h=lines.Count*lineHeight+16;
        int x=Math.Clamp(hit.Bounds.Center.X-w/2,8,Math.Max(8,Game1.uiViewport.Width-w-8));
        int y=Math.Max(8,hit.Bounds.Top-h-7);
        Card(b,new(x+2,y+3,w,h),Color.Black*.15f,Color.Black*.03f);
        Card(b,new(x,y,w,h),Paper,Border);
        for(int i=0;i<lines.Count;i++)Text(b,lines[i],x+10,y+8+i*lineHeight,CitationBlue,.72f);
    }

    private void RebuildTodos()
    {
        int errorSpace = todos.Error.Length > 0 ? 26 : 0;
        todoArea = new(history.X, history.Y + errorSpace, history.Width, Math.Max(1, history.Height - errorSpace));
        if (todoRevision != todos.Revision || todoWidth != todoArea.Width)
        {
            var previous = todoLayouts.ToDictionary(row => row.Index, TodoOffset);
            bool animate = todoRevision >= 0 && todoWidth == todoArea.Width;
            todoLayouts.Clear(); todoHeight = 0;
            var visible = todos.VisibleIndices;
            foreach (int i in visible)
            {
                List<string> lines = Wrap(todos.Items[i].Text, Math.Max(1, todoArea.Width - 65));
                int h = Math.Max(48, lines.Count * LineHeight + 24);
                todoLayouts.Add(new(i, lines, todoHeight, h)); todoHeight += h + 8;
            }
            if (todoLayouts.Count > 0) todoHeight -= 8;
            todoAnimationFrom = animate ? todoLayouts.ToDictionary(row => row.Index,
                row => previous.TryGetValue(row.Index, out float top) ? top : row.Top - 8f) : new();
            todoAnimationStartedAt = Environment.TickCount64;
            todoRevision = todos.Revision; todoWidth = todoArea.Width;
        }
        todoScroll = Math.Clamp(todoScroll, 0, Math.Max(0, todoHeight - todoArea.Height));
    }
    private void DrawTodos(SpriteBatch b)
    {
        RebuildTodos();
        if (todos.Error.Length > 0)
            Text(b, Fit(todos.Error, history.Width, .72f), history.X + 2, history.Y + 2, new Color(158, 102, 74), .72f);
        if (todos.Items.Count == 0 && todos.Error.Length == 0)
        {
            CenterText(b, "把今天想做的事记在这里", todoArea.Center.Y - 25, Ink, .94f);
        }
        foreach (var row in todoLayouts)
        {
            TodoItem item = todos.Items[row.Index];
            int top = todoArea.Y + (int)TodoOffset(row) - todoScroll;
            float progress = row.Index == animatedTodoIndex ? TodoProgress : 1f;
            Card(b, new(todoArea.X, top, todoArea.Width - 8, row.Height),
                item.Completed ? new Color(240, 237, 228) : new Color(255, 252, 245), Border * .65f, todoArea);
            var box = new Rectangle(todoArea.X + 10, top + 12, 22, 22);
            if (box.Top >= todoArea.Top && box.Bottom <= todoArea.Bottom)
            {
                Card(b, box, item.Completed ? Accent * .65f : Paper, item.Completed ? Accent * .65f : Border);
                if (item.Completed)
                {
                    for (int i = 0; i < 4; i++) Fill(b, new(box.X + 4 + i, box.Y + 10 + i, 2, 2), Paper * progress);
                    for (int i = 0; i < 8; i++) Fill(b, new(box.X + 8 + i, box.Y + 13 - i, 2, 2), Paper * progress);
                }
            }
            for (int i = 0; i < row.Lines.Count; i++)
            {
                int y = top + 12 + i * LineHeight;
                if (y < todoArea.Top || y + LineHeight > todoArea.Bottom) continue;
                Text(b, row.Lines[i], todoArea.X + 44, y, item.Completed ? Color.Lerp(Ink, Muted, progress) : Ink);
                if (item.Completed) Fill(b, new(todoArea.X + 44, y + LineHeight / 2,
                    (int)(font.Measure(row.Lines[i], TextScale).X * progress), 1), Muted);
            }
        }
        if (todoHeight > todoArea.Height)
        {
            int thumb = Math.Min(todoArea.Height, Math.Max(20, todoArea.Height * todoArea.Height / todoHeight));
            int y = todoArea.Y + (todoArea.Height - thumb) * todoScroll / (todoHeight - todoArea.Height);
            Fill(b, new(todoArea.Right - 3, y, 3, thumb), Accent * .4f);
        }
        if (TodoProgress >= 1) animatedTodoIndex = -1;
    }
    private float TodoProgress => Math.Clamp((Environment.TickCount64 - todoAnimationStartedAt) / 240f, 0, 1);
    private float TodoOffset(TodoLayout row)
    {
        if (!todoAnimationFrom.TryGetValue(row.Index, out float from)) return row.Top;
        float t = 1 - MathF.Pow(1 - TodoProgress, 3);
        return MathHelper.Lerp(from, row.Top, t);
    }
    private sealed record TodoLayout(int Index, List<string> Lines, int Top, int Height);

    private int EditorCapacity => Math.Max(1, (editor.Height - 18) / LineHeight);
    private int DisplayCaret => composition.Length > 0 && Draft.AllSelected
        ? composition.Length : Draft.Cursor + composition.Length;

    private EditorDocument EditorLines()
    {
        string text = Draft.Text;
        if (composition.Length > 0) text = Draft.AllSelected ? composition
            : text.Insert(Draft.Cursor, composition);
        int available = Math.Max(1, editor.Width - 28);
        if (editorDocument is { } cached && cached.Text == text && cached.Width == available) return cached;
        var lines = new List<EditorLine>();
        int start = 0;
        string line = "";
        foreach (int offset in StringInfo.ParseCombiningCharacters(text))
        {
            string element = StringInfo.GetNextTextElement(text, offset);
            if (element == "\n")
            {
                lines.Add(new(start, offset, line)); start = offset + 1; line = "";
                continue;
            }
            if (line.Length > 0 && font.Measure(line + element, TextScale).X > available)
            {
                lines.Add(new(start, offset, line)); start = offset; line = "";
            }
            line += element;
        }
        lines.Add(new(start, text.Length, line));
        return editorDocument = new(text, available, lines);
    }
    private void RefreshEditorLayout()
    {
        if (selectedTab == 2) return;
        int rows = Math.Min(3, EditorLines().Lines.Count);
        int desired = Math.Max(44, rows * LineHeight + 18);
        int editorY = selectedTab == 1 ? panel.Y + 108 : footer.Y - 10 - desired;
        int historyY = selectedTab == 1 ? editorY + desired + 16 : panel.Y + 108;
        int historyBottom = selectedTab == 1 ? footer.Y - 12 : editorY - 16;
        int historyHeight = Math.Max(1, historyBottom - historyY);
        if (editor.Height == desired && editor.Y == editorY && history.Y == historyY && history.Height == historyHeight) return;
        editor.Height = desired;
        editor.Y = editorY;
        send.Y = editor.Bottom - send.Height;
        history.Y = historyY;
        history.Height = historyHeight;
        revision = -1;
    }
    private static int EditorRow(EditorDocument doc, int cursor) =>
        doc.Lines.FindLastIndex(line => line.Start <= cursor);
    private void FollowEditorCaret(EditorDocument doc)
    {
        int row = EditorRow(doc, DisplayCaret);
        firstEditorRow = Math.Clamp(firstEditorRow, 0, Math.Max(0, doc.Lines.Count - EditorCapacity));
        if (row < firstEditorRow) firstEditorRow = row;
        if (row >= firstEditorRow + EditorCapacity) firstEditorRow = row - EditorCapacity + 1;
    }
    private int HitEditorLine(EditorLine line, float x)
    {
        int[] offsets = StringInfo.ParseCombiningCharacters(line.Text).Append(line.Text.Length).ToArray();
        return line.Start + offsets.MinBy(i => Math.Abs(font.Measure(line.Text[..i], TextScale).X - x));
    }
    private void MoveEditorVertical(int direction)
    {
        var doc = EditorLines();
        int row = EditorRow(doc, Draft.Cursor);
        EditorLine current = doc.Lines[row];
        preferredEditorX ??= font.Measure(doc.Text[current.Start..Draft.Cursor], TextScale).X;
        int target = row + direction;
        if (target < 0) Draft.MoveTo(0);
        else if (target >= doc.Lines.Count) Draft.MoveTo(Draft.Text.Length);
        else Draft.MoveTo(HitEditorLine(doc.Lines[target], preferredEditorX.Value));
    }
    private void DrawEditor(SpriteBatch b)
    {
        Card(b, editor, new Color(255, 253, 247), Border);
        var doc = EditorLines();
        FollowEditorCaret(doc);
        int caretRow = EditorRow(doc, DisplayCaret);
        int markStart = Draft.AllSelected ? 0 : Draft.Cursor;
        int markEnd = markStart + composition.Length;
        for (int i = firstEditorRow; i < Math.Min(doc.Lines.Count, firstEditorRow + EditorCapacity); i++)
        {
            EditorLine line = doc.Lines[i];
            int y = editor.Y + 9 + (i - firstEditorRow) * LineHeight;
            if (Draft.AllSelected && composition.Length == 0)
                Fill(b, new(editor.X + 11, y, Math.Min(doc.Width, (int)font.Measure(line.Text, TextScale).X + 4), LineHeight), new Color(214, 225, 200));
            if (doc.Text.Length == 0) Text(b, selectedTab == 1 ? "新建待办……" : "问问小助手……", editor.X + 13, y, Muted);
            else
            {
                int a = Math.Clamp(markStart - line.Start, 0, line.Text.Length);
                int z = Math.Clamp(markEnd - line.Start, a, line.Text.Length);
                string before = line.Text[..a], marked = line.Text[a..z], after = line.Text[z..];
                float x = editor.X + 13;
                Text(b, before, x, y, Ink); x += font.Measure(before, TextScale).X;
                Text(b, marked, x, y, Accent);
                if (marked.Length > 0) Fill(b, new((int)x, y + LineHeight - 2, (int)font.Measure(marked, TextScale).X, 1), Accent);
                x += font.Measure(marked, TextScale).X;
                Text(b, after, x, y, Ink);
            }
            if (i == caretRow)
            {
                int offset = Math.Clamp(DisplayCaret - line.Start, 0, line.Text.Length);
                int x = editor.X + 13 + (int)font.Measure(line.Text[..offset], TextScale).X;
                SetCandidateAnchor(new Rectangle(x, y, 1, LineHeight));
                if (composition.Length == 0 && !Draft.AllSelected && (Environment.TickCount64 - caretMovedAt) % 1000 < 550)
                    Fill(b, new(x, y + 2, 1, LineHeight - 5), Accent);
            }
        }
        if (doc.Lines.Count > EditorCapacity)
            Fill(b, new(editor.Right - 5, editor.Y + 5 + (editor.Height - 20) * firstEditorRow / Math.Max(1, doc.Lines.Count - EditorCapacity), 2, 10), Border);
        Card(b, send, Accent, new Color(77, 99, 68));
        Icon(b, selectedTab == 1 ? "add" : session.IsBusy && ChatCommands.Parse(Draft.Text).Kind == ChatCommandKind.None ? "stop" : "send", send.X + 14, send.Y + 14, Paper);
    }
    private sealed record EditorLine(int Start, int End, string Text);
    private sealed record EditorDocument(string Text, int Width, List<EditorLine> Lines);

    private int CommandPopupRow=>Math.Max(34,LineHeight+10);
    private const int CommandPopupHeader=26;
    private Rectangle CommandPopupBounds()
    {
        int available=Math.Max(CommandPopupRow,editor.Y-(panel.Y+104)-CommandPopupHeader-6);
        int rows=Math.Clamp(available/CommandPopupRow,1,CommandCompletion.Entries.Count);
        int h=CommandPopupHeader+rows*CommandPopupRow+4;
        return new(editor.X,editor.Y-6-h,send.Right-editor.X,h);
    }
    private int CommandPopupFirst(Rectangle bounds)
    {
        int visible=Math.Max(1,(bounds.Height-CommandPopupHeader-4)/CommandPopupRow);
        return Math.Clamp(commandCompletion.Selected-visible+1,0,Math.Max(0,CommandCompletion.Entries.Count-visible));
    }
    private void CompleteCommand(int? index=null)
    {
        string? completed=commandCompletion.Complete(index);
        if(completed==null)return;
        session.Draft.Replace(completed);preferredEditorX=null;caretMovedAt=Environment.TickCount64;
        RefreshEditorLayout();RefreshCommandCompletion();
    }
    private void DrawCommandPopup(SpriteBatch b)
    {
        Rectangle bounds=CommandPopupBounds();
        Card(b,new(bounds.X+2,bounds.Y+3,bounds.Width,bounds.Height),Color.Black*.13f,Color.Black*.02f);
        Card(b,bounds,Paper,Border);
        Text(b,Fit("特殊指令  ↑↓ 选择 · Enter 补全",bounds.Width-24,.64f),bounds.X+12,bounds.Y+6,Muted,.64f);
        int first=CommandPopupFirst(bounds);
        int visible=(bounds.Height-CommandPopupHeader-4)/CommandPopupRow;
        for(int row=0;row<visible&&first+row<CommandCompletion.Entries.Count;row++)
        {
            int index=first+row;
            var entry=CommandCompletion.Entries[index];
            var r=new Rectangle(bounds.X+4,bounds.Y+CommandPopupHeader+row*CommandPopupRow,bounds.Width-8,CommandPopupRow);
            if(index==commandCompletion.Selected)Fill(b,r,new Color(229,235,214));
            float nameWidth=font.Measure(entry.Name,.76f).X;
            Text(b,Fit(entry.Name,r.Width-16,.76f),r.X+9,r.Y+6,Ink,.76f);
            int descriptionX=r.X+Math.Max(122,(int)nameWidth+23);
            if(r.Right-descriptionX>36)Text(b,Fit(entry.Description,r.Right-descriptionX-8,.68f),descriptionX,r.Y+7,Muted,.68f);
        }
    }

    private void RefreshGameBar()
    {
        gameBarRefreshedAt=Environment.TickCount64;
        if(!Context.IsWorldReady){gameBarValues=Array.Empty<string>();return;}
        string season = Game1.currentSeason switch { "spring" => "春季", "summer" => "夏季", "fall" => "秋季", "winter" => "冬季", _ => Game1.currentSeason };
        string weather = Game1.player.currentLocation.GetWeather().Weather switch
        { "Sun" => "晴", "Rain" => "雨", "Storm" => "雷雨", "Snow" => "雪", "Wind" => "风", "GreenRain" => "绿雨", _ => Game1.player.currentLocation.GetWeather().Weather };
        string weekday = new[] { "星期天", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" }[Game1.dayOfMonth % 7];
        gameBarValues = new[] { $"{season}第 {Game1.dayOfMonth} 日 {weekday}", $"{Game1.timeOfDay / 100:00}:{Game1.timeOfDay % 100:00}", weather };
    }

    private void DrawGameBar(SpriteBatch b)
    {
        if (!Context.IsWorldReady) { Text(b, "进入农场后显示游戏状态", footer.X, footer.Y + 3, Muted, .68f); return; }
        string[] values=gameBarValues;
        string[] icons = { "sprout", "clock", "sun" };
        float scale = footer.Width < 370 ? .6f : .7f;
        int x = footer.X;
        for (int i = 0; i < values.Length; i++)
        {
            int w = 23 + (int)font.Measure(values[i], scale).X;
            if (x + w > footer.Right) break;
            Icon(b, icons[i], x, footer.Y + 5, Muted);
            Text(b, values[i], x + 22, footer.Y + 3, Muted, scale);
            x += w + (footer.Width < 440 ? 12 : 24);
        }
    }

    private void CenterText(SpriteBatch b, string text, int y, Color color, float scale)
    {
        text = Fit(text, Math.Max(1, panel.Width - 44), scale);
        Text(b, text, panel.Center.X - font.Measure(text, scale).X / 2, y, color, scale);
    }
    private void Text(SpriteBatch b, string text, float x, float y, Color color, float scale = TextScale, bool bold = false) => font.Draw(b, text, x, y, color, scale, bold);
    private static void Fill(SpriteBatch b, Rectangle rect, Color color)
    { if (rect.Width > 0 && rect.Height > 0) b.Draw(Game1.staminaRect, rect, color); }

    // Small stepped corners and hand-drawn icons retain a light game UI character.
    private static void Card(SpriteBatch b, Rectangle r, Color fill, Color edge, Rectangle? clip = null)
    {
        void Part(Rectangle p, Color c) => Fill(b, clip.HasValue ? Rectangle.Intersect(p, clip.Value) : p, c);
        Part(new(r.X + 3, r.Y, r.Width - 6, r.Height), edge);
        Part(new(r.X, r.Y + 3, r.Width, r.Height - 6), edge);
        Part(new(r.X + 3, r.Y + 1, r.Width - 6, r.Height - 2), fill);
        Part(new(r.X + 1, r.Y + 3, r.Width - 2, r.Height - 6), fill);
    }
    private static void Icon(SpriteBatch b, string kind, int x, int y, Color c)
    {
        void P(int px, int py, int w, int h) => Fill(b, new(x + px, y + py, w, h), c);
        switch (kind)
        {
            case "star": P(7,0,2,16); P(0,7,16,2); P(5,5,6,6); P(6,3,4,10); P(3,6,10,4); break;
            case "close": for (int i=0;i<7;i++) { P(i*2,i*2,2,2); P(12-i*2,i*2,2,2); } break;
            case "chat":
                P(3,1,12,2); P(1,3,2,9); P(15,3,2,9); P(3,12,12,2);
                P(3,14,2,3); P(5,14,2,1); P(5,6,2,2); P(10,6,2,2); break;
            case "todo":
                P(3,0,12,2); P(3,16,12,2); P(0,3,2,12); P(16,3,2,12);
                P(1,1,2,2); P(15,1,2,2); P(1,15,2,2); P(15,15,2,2);
                P(4,8,2,2); P(6,10,2,2); P(8,8,2,2); P(10,6,2,2); P(12,4,2,2); break;
            case "help":
                P(3,0,12,2); P(3,16,12,2); P(0,3,2,12); P(16,3,2,12);
                P(1,1,2,2); P(15,1,2,2); P(1,15,2,2); P(15,15,2,2);
                P(6,4,5,2); P(5,5,2,2); P(10,6,2,2); P(8,8,3,2); P(8,9,2,2); P(8,13,2,2); break;
            case "pin": P(4,0,8,2); P(5,2,6,6); P(2,8,12,2); P(7,10,2,6); break;
            case "sprout": P(7,6,2,10); P(1,1,5,3); P(3,4,5,3); P(10,0,5,3); P(8,3,5,3); break;
            case "clock": P(4,0,8,2); P(4,14,8,2); P(0,4,2,8); P(14,4,2,8); P(2,2,2,2); P(12,2,2,2); P(2,12,2,2); P(12,12,2,2); P(7,3,2,6); P(9,7,3,2); break;
            case "sun": P(5,5,6,6); P(7,0,2,3); P(7,13,2,3); P(0,7,3,2); P(13,7,3,2); P(2,2,2,2); P(12,2,2,2); P(2,12,2,2); P(12,12,2,2); break;
            case "coin": P(3,1,10,14); P(1,4,14,8); P(7,4,2,8); break;
            case "add": P(7,1,2,14); P(1,7,14,2); break;
            case "stop": P(2,2,12,12); break;
            case "send": for(int i=0;i<7;i++) P(i*2, i,2,16-i*2); P(0,7,10,2); break;
        }
    }
    private List<string> Wrap(string text, int maxWidth, float scale = TextScale, bool bold = false)
    {
        var lines = new List<string>(); var line = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text.Replace("\r", ""));
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (element == "\n") { lines.Add(line.ToString()); line.Clear(); continue; }
            if (line.Length > 0 && font.Measure(line.ToString() + element, scale, bold).X > maxWidth)
            { lines.Add(line.ToString()); line.Clear(); }
            line.Append(element);
        }
        lines.Add(line.ToString()); return lines;
    }
    private string Fit(string text, int width, float scale)
    {
        if (font.Measure(text, scale).X <= width) return text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int count = starts.Length;
        while (count > 0 && font.Measure(text[..starts[--count]] + "…", scale).X > width) { }
        return count == 0 ? "…" : text[..starts[count]] + "…";
    }
    protected override void cleanupBeforeExit() => Dispose();
    public override void emergencyShutDown() => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; input.Dispose(); font.Dispose(); Game1.keyboardDispatcher.Discard();
        if (Game1.keyboardDispatcher.Subscriber == null && previousSubscriber != null) Game1.keyboardDispatcher.Subscriber = previousSubscriber;
    }
    private sealed record RichLine(List<CitationPart> Runs);
    private sealed record MessageLayout(string Role, List<RichLine> Lines, int Top, int Height, int Width);
}
