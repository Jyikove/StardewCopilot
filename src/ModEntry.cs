using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewCodexChat.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewCodexChat.Input;

namespace StardewCodexChat;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private ChatSession session = null!;
    private ChatMenu? menu;
    private KeyConfigMenu? keyMenu;
    private Core.FileSecretStore secrets = null!;
    private bool requestKeyConfiguration;
    private Core.TodoList? todos;
    private string? todoSaveKey;
    private Core.MainThreadQueries queries = null!;
    private Core.ConversationStore conversations = null!;
    private readonly HudChatButton hudButton = new();
    private readonly MenuLayer menuLayer = new();
    private readonly PendingMenuOpen pendingOpen = new();
    private readonly Vision.GameScreenCapture screenCapture = new();

    public override void Entry(IModHelper helper)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Monitor.Log("Stardew Copilot 当前仅支持 macOS。", LogLevel.Error);
            return;
        }
        queries = new Core.MainThreadQueries(ReadAssistantTool);
        conversations = new Core.ConversationStore(Path.Combine(helper.DirectoryPath, "data", "conversations"));
        secrets = new Core.FileSecretStore(Path.Combine(helper.DirectoryPath, "secrets.json"));
        config = helper.ReadConfig<ModConfig>();
        var knowledge = new Knowledge.KnowledgeCacheManager(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","StardewCopilot","knowledge.sqlite"),config.Knowledge,
            message => Monitor.Log(message,LogLevel.Trace));
        var broad = new Knowledge.BroadWebSearch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","StardewCopilot","knowledge.sqlite"),secrets.GetKeyAsync,
            message=>Monitor.Log(message,LogLevel.Trace), options:config.Knowledge);
        var retrieval = new Knowledge.KnowledgeRouter(knowledge,broad,config.Knowledge?.EnableBroadWeb??true);
        _ = knowledge.InitializeAsync();
        session = new ChatSession(new Core.DeepSeekClient(secrets, knowledge: retrieval.RetrieveAsync, diagnostic:message=>Monitor.Log(message,LogLevel.Trace), cachedKnowledge:retrieval.TryCacheAsync, freshKnowledge:retrieval.RetrieveFreshAsync, uncachedKnowledge:retrieval.RetrieveUncachedAsync), queries, conversations, () => todos, screenCapture, new Vision.ScreenImageProcessor());
        session.KeyConfigurationRequested += () => requestKeyConfiguration = true;
        if (string.IsNullOrWhiteSpace(config.Model) || config.Model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            config.Model = "deepseek-flash";
        helper.WriteConfig(config);
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Display.RenderedHud += (_, e) => hudButton.Draw(e.SpriteBatch);
        // With the assistant closed, a permitted recapture uses the next rendered game frame.
        helper.Events.Display.Rendered += (_, e) => { if (!menuLayer.IsOpen) screenCapture.BeforeOverlay(e.SpriteBatch); };
        helper.Events.Display.WindowResized += (_, e) => menuLayer.Resize(new Rectangle(0,0,e.OldSize.X,e.OldSize.Y),new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height));
        helper.Events.GameLoop.UpdateTicked += OnUpdate;
        helper.Events.GameLoop.SaveLoaded += (_, _) => BindConversation();
        helper.Events.GameLoop.DayStarted += (_, _) => BindConversation(onlyIfChanged: true);
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { screenCapture.Cancel(); pendingOpen.Cancel(); menuLayer.Close(); menu?.Dispose(); menu = null; keyMenu?.Dispose(); keyMenu = null; requestKeyConfiguration = false; session.SwitchSave(null); todos = null; todoSaveKey = null; };
        helper.Events.GameLoop.GameLaunched += (_, _) =>
            GameRunner.instance.Exiting += (_, _) => { screenCapture.Cancel(); pendingOpen.Cancel(); menuLayer.Close(); menu?.Dispose(); keyMenu?.Dispose(); hudButton.Dispose(); session.SwitchSave(null); conversations.FlushOnExit(); };
        helper.ConsoleCommands.Add("copilot_chat", "Open Stardew Copilot.", (_, _) => Open());
        helper.ConsoleCommands.Add("codex_chat", "Open Stardew Copilot (legacy alias).", (_, _) => Open());
        helper.ConsoleCommands.Add("copilot_check", "Check read-only game queries locally without sending data to DeepSeek.", (_, _) =>
        {
            foreach (var query in new[] { ("get_overview", "{}"), ("get_inventory", "{}"), ("get_quests", "{}"),
                ("get_npc", "{\"name\":\"Abigail\"}"), ("get_crops", "{}"), ("get_machines", "{}"), ("find_item", "{\"query\":\"(O)378\"}") })
            {
                try
                {
                    string result = State.GameStateReader.Read(query.Item1, query.Item2);
                    using var json = System.Text.Json.JsonDocument.Parse(result);
                    bool ready = Context.IsWorldReady;
                    bool error = json.RootElement.TryGetProperty("error", out _) ||
                        (json.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("error", out _));
                    Monitor.Log($"{query.Item1}: {(ready ? error ? "unavailable" : "OK" : "no save loaded")} ({result.Length} chars)", error ? LogLevel.Warn : LogLevel.Info);
                }
                catch { Monitor.Log($"{query.Item1}: unavailable", LogLevel.Warn); }
            }
        });
    }

    private string ReadAssistantTool(string name, string arguments)
    {
        if (name is "get_todos" or "add_todo")
        {
            if (!Context.IsWorldReady || todos == null) return "{\"error\":\"No save loaded; todo access unavailable.\"}";
            return todos.ExecuteTool(name, arguments);
        }
        return State.GameStateReader.Read(name, arguments);
    }

    private void BindConversation(bool onlyIfChanged = false)
    {
        if (!Context.IsWorldReady) return;
        string key = $"{Constants.SaveFolderName ?? "farmhand"}:{Game1.uniqueIDForThisGame}:{Game1.player.UniqueMultiplayerID}";
        if (!onlyIfChanged || !session.IsBoundTo(key)) { pendingOpen.Cancel(); session.SwitchSave(key); }
        if (todoSaveKey != key)
        {
            todos = new Core.TodoList(Path.Combine(Helper.DirectoryPath, "data", "todos"), key);
            todoSaveKey = key;
        }
    }

    [EventPriority(EventPriority.High)]
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.IsSuppressed()) return;
        if (e.Button == SButton.Enter && config.OpenChat.JustPressed() && CanOpen(Keys.Enter))
        {
            Helper.Input.Suppress(SButton.Enter);
            Open();
            return;
        }
        if (e.Button != SButton.MouseLeft) return;
        if (!CanOpen(Keys.None)) return;
        if (!hudButton.HitTest(Game1.getMouseX(true), Game1.getMouseY(true))) return;
        Helper.Input.Suppress(SButton.MouseLeft);
        Open(Keys.None);
    }

    [EventPriority(EventPriority.High)]
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (menuLayer.IsOpen)
        {
            foreach (SButton button in e.Pressed.Concat(e.Held))
                if (button.TryGetKeyboard(out _)) Helper.Input.Suppress(button);
            return;
        }
        if (!config.OpenChat.JustPressed()) return;
        Keys trigger=e.Pressed.Select(button=>button.TryGetKeyboard(out var key)?key:Keys.None)
            .FirstOrDefault(key=>key is not (Keys.None or Keys.LeftControl or Keys.RightControl or Keys.LeftShift or Keys.RightShift or Keys.LeftAlt or Keys.RightAlt or Keys.LeftWindows or Keys.RightWindows));
        if (!CanOpen(trigger)) return;
        Helper.Input.SuppressActiveKeybinds(config.OpenChat);
        Open(trigger);
    }

    private void Open(Keys trigger=Keys.Enter)
    {
        if (!CanOpen(trigger) || !Context.IsGameLaunched) return;
        try
        {
            BindConversation(onlyIfChanged: true);
            pendingOpen.Start(todoSaveKey!, trigger, () => secrets.GetKeyAsync(CancellationToken.None));
        }
        catch (Exception error)
        {
            HandleOpenError(error);
        }
    }

    private void HandleOpenError(Exception error)
    {
        pendingOpen.Cancel(); menuLayer.Close();
        menu?.Dispose(); menu = null; keyMenu?.Dispose(); keyMenu = null;
        Monitor.Log($"无法打开 Stardew Copilot：{error.GetType().Name}: {error.Message}", LogLevel.Error);
    }

    private void ShowChat()
    {
        keyMenu = null;
        menu = (ChatMenu)menuLayer.Show(() => new ChatMenu(session, config.GetChatOptions(), todos!) { BeforeDraw = screenCapture.BeforeOverlay });
    }

    private void ShowKeyConfiguration()
    {
        menu = null; keyMenu = null;
        keyMenu = (KeyConfigMenu)menuLayer.Show(()=>new KeyConfigMenu(secrets, () =>
        {
            keyMenu = null;
            if (!Context.IsWorldReady) {menuLayer.Close();return;}
            ShowChat();
        }));
    }

    private bool CanOpen(Keys trigger=Keys.Enter) => !pendingOpen.IsPending&&!menuLayer.IsOpen&&MenuOpenPolicy.CanOpen(trigger,allowOverMenu:trigger==Keys.Enter);

    private void OnUpdate(object? sender, UpdateTickedEventArgs e)
    {
        menuLayer.Tick();
        queries.Tick();
        session.Tick();
        try
        {
            if (pendingOpen.TryComplete(todoSaveKey, out bool configured))
            {
                if (configured) ShowChat();
                else ShowKeyConfiguration();
            }
        }
        catch (Exception error) { HandleOpenError(error); }
        if (requestKeyConfiguration)
        {
            requestKeyConfiguration = false;
            if (Context.IsWorldReady && menuLayer.IsShowing(menu)) ShowKeyConfiguration();
        }
        if (keyMenu != null && !menuLayer.IsShowing(keyMenu)) { keyMenu.Dispose(); keyMenu = null; }
        if (menu != null && !menuLayer.IsShowing(menu))
        {
            menu.Dispose();
            menu = null;
        }
    }
}
