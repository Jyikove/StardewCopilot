using System.Collections.Concurrent;
using System.Text;
using StardewCodexChat.Core;

namespace StardewCodexChat;

/// <summary>Main-thread UI state; background work uses detached conversation snapshots.</summary>
internal sealed class ChatSession
{
    private sealed record Reply(string Answer, ConversationState State, bool SaveFailed);
    private readonly Conversation conversation = new();
    private readonly DeepSeekClient runner;
    private readonly QueryActivity? queries;
    private readonly ConversationStore? store;
    private readonly Func<TodoList?>? todoList;
    private readonly IScreenCapture? screenCapture;
    private readonly IScreenImageProcessor? screenImages;
    private bool analyzingScreen;
    private readonly object bindingGate = new();
    private string? saveKey;
    private long generation;
    private bool storageBlocked;
    private Task<LoadedConversation>? loading;
    private Task? resetSaving;
    private bool resetNotice;
    private Task<Reply>? pending;
    private ConcurrentQueue<string>? chunks;
    private int answerIndex = -1;
    private long lastPaint;
    private CancellationTokenSource? cancellation;
    private string question = "";
    internal ChatSession(DeepSeekClient? client = null, IGameQueries? queries = null, ConversationStore? store = null, Func<TodoList?>? todoList = null,
        IScreenCapture? screenCapture = null, IScreenImageProcessor? screenImages = null)
    { runner = client ?? new DeepSeekClient(); this.queries = queries == null ? null : new QueryActivity(queries); this.store = store; this.todoList = todoList; this.screenCapture = screenCapture; this.screenImages = screenImages; }
    internal event Action? KeyConfigurationRequested;
    internal readonly TextBuffer Draft = new();
    internal readonly List<ChatMessage> Display = new();
    internal bool IsBusy => pending != null || loading != null || resetSaving != null;
    // Presentation only: observe existing work without changing requests or results.
    internal string AssistantActivity => !IsBusy ? "正在待命"
        : queries?.IsReading == true ? "正在读取游戏状态"
        : pending != null && answerIndex >= 0 ? "正在回答" : analyzingScreen ? "正在分析画面" : "正在思考";
    private sealed class QueryActivity(IGameQueries source) : IGameQueries
    {
        private int active;
        internal bool IsReading => Volatile.Read(ref active) > 0;
        public async Task<string> QueryAsync(string name, string arguments, CancellationToken token)
        {
            Interlocked.Increment(ref active);
            try { return await source.QueryAsync(name, arguments, token); }
            finally { Interlocked.Decrement(ref active); }
        }
    }
    internal bool IsBoundTo(string key) => saveKey == key;
    internal int Revision { get; private set; }
    internal string Status { get; private set; } = "当前存档的对话会自动保存";

    internal void SwitchSave(string? key)
    {
        StopWork(); ClearMemory();
        lock (bindingGate) saveKey = key;
        if (key != null && store != null) { loading = store.LoadAsync(key); Status = "正在恢复此存档的对话…"; }
        Revision++;
    }

    internal bool Send(ChatOptions options)
    {
        if (string.IsNullOrWhiteSpace(Draft.Text)) return false;
        var command = ChatCommands.Parse(Draft.Text);
        if (command.Kind == ChatCommandKind.ResetKey)
        {
            if (loading != null || resetSaving != null) return false;
            StopWork(); Draft.Clear(); KeyConfigurationRequested?.Invoke(); return true;
        }
        if (command.Kind == ChatCommandKind.ClearChat) { Reset(showConfirmation: true); return true; }
        if (command.Kind == ChatCommandKind.ClearTodo)
        {
            if (loading != null || resetSaving != null) { Notice("正在加载或清理对话，请稍后再执行。", false); return false; }
            var list = todoList?.Invoke();
            if (list == null) { Notice("请先进入存档，再清空待办。", false); return false; }
            bool wasReplying = pending != null;
            StopWork();
            if (list.Clear()) { Notice(wasReplying ? "已清除当前存档的全部待办，并停止当前回复。" : "已清除当前存档的全部待办。", true); return true; }
            Notice(list.Error, false); return false;
        }
        if (command.Kind == ChatCommandKind.Invalid) { Notice("未知指令或参数不正确。" + ModHelp.Usage, true); return true; }
        if (command.Kind == ChatCommandKind.Help && command.Argument.Length == 0) { Notice(ModHelp.Usage, true); return true; }
        if (command.Kind == ChatCommandKind.Search && command.Argument.Length == 0) { Notice("用法：/search 你的问题", false); return false; }
        if (command.Kind == ChatCommandKind.Screen && command.Argument.Length == 0) { Notice("用法：/screen 你想了解当前画面的什么内容", false); return false; }
        if (command.Kind == ChatCommandKind.Screen && (screenCapture == null || screenImages == null)) { Notice("截图接口未就绪，请更新 Mod 并重启游戏。", false); return false; }
        if (IsBusy || storageBlocked) return false;
        string? helpQuestion = command.Kind == ChatCommandKind.Help ? command.Argument : null;
        bool research = command.Kind == ChatCommandKind.Search;
        bool screen = command.Kind == ChatCommandKind.Screen;
        analyzingScreen = screen;
        string? screenQuestion = screen ? command.Argument : null;
        if(research)options=options with {TimeoutSeconds=60};
        string? searchQuestion=research?command.Argument:null;
        question = Draft.Text.Trim();
        conversation.BuildMessages(question); // Validate before changing the UI.
        var snapshot = conversation.Snapshot();
        string currentQuestion = question;
        long currentGeneration;
        string? currentKey;
        lock (bindingGate) { currentGeneration = generation; currentKey = saveKey; }
        Draft.Clear(); Display.Add(new("user", question));
        if (Display.Count > 80) Display.RemoveRange(0, Display.Count - 80);
        Status = "正在回复…";
        cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        var incoming = new ConcurrentQueue<string>(); chunks = incoming; answerIndex = -1; lastPaint = 0;
        pending = Task.Run(async () => {
            var totalTimer=System.Diagnostics.Stopwatch.StartNew();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 600)));
            var working = new Conversation(); working.Restore(snapshot);
            try
            {
                if (working.NeedsCompression(currentQuestion, options.MemoryTokenBudget))
                {
                    int prefix = working.CompressionPrefix(currentQuestion, options.MemoryTokenBudget);
                    string summary = await runner.SummarizeAsync(options, working.Summary, working.Messages.Take(prefix).ToArray(), deadline.Token);
                    working.ApplySummary(summary, prefix);
                }
                var replyOptions=research?options with {TimeoutSeconds=Math.Max(1,ResearchBudget.TotalSeconds-(int)Math.Ceiling(totalTimer.Elapsed.TotalSeconds))}:options;
                string answer = screen ? await runner.AskScreenAsync(replyOptions, screenQuestion!, screenCapture!, screenImages!, deadline.Token, incoming.Enqueue,
                        context: working.Snapshot(), queries: queries)
                    : await runner.AskAsync(replyOptions, helpQuestion != null ? ModHelp.Messages(helpQuestion)
                    : research ? Conversation.BuildSearchMessages(searchQuestion!) : working.BuildMessages(currentQuestion),
                    deadline.Token, incoming.Enqueue, helpQuestion != null ? null : queries, research);
                working.AddExchange(currentQuestion, answer);
                var state = working.Snapshot();
                Task? saving = null;
                lock (bindingGate)
                {
                    if (generation != currentGeneration || token.IsCancellationRequested) throw new OperationCanceledException(token);
                    if (store != null && currentKey != null) saving = store.SaveAsync(currentKey, state);
                }
                bool failed = false;
                if (saving != null) try { await saving; } catch { failed = true; }
                return new Reply(answer, state, failed);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("本次回答或上下文整理超时，原有记录已保留，请重试。"); }
        });
        Revision++; return true;
    }

    internal void Tick()
    {
        if (resetSaving is { IsCompleted: true })
        {
            try { resetSaving.GetAwaiter().GetResult(); if (resetNotice) Display.Add(new("system", "已清除当前存档的全部历史对话。")); }
            catch { storageBlocked = true; Display.Add(new("system", "清除对话未完成，部分磁盘记录可能仍存在；请检查 Mod 文件夹权限后重试 /clearchat。")); }
            resetSaving = null; Revision++;
        }
        if (loading is { IsCompleted: true })
        {
            try
            {
                var loaded = loading.GetAwaiter().GetResult(); conversation.Restore(loaded.State);
                Display.AddRange(loaded.State.Transcript.TakeLast(80));
                Status = "已恢复此存档的对话，可以继续提问";
                if (loaded.RecoveredBackup) Display.Add(new("system", "对话主文件不可用，已从 Mod 文件夹内的备份恢复。"));
            }
            catch
            {
                storageBlocked = true;
                Display.Add(new("system", "此存档的对话文件无法恢复，已保留原文件。请检查 Mod 内 data/conversations 文件夹，或输入 /clearchat 清除后重新开始。"));
                Status = "对话恢复失败";
            }
            loading = null; Revision++;
        }
        if (pending == null) return;
        if (pending.IsCompleted || Environment.TickCount64 - lastPaint >= 100)
        {
            var text = new StringBuilder();
            while (chunks != null && chunks.TryDequeue(out string? chunk)) text.Append(chunk);
            if (text.Length > 0)
            {
                SetAnswer((answerIndex >= 0 ? Display[answerIndex].Text : "") + text);
                Status = "正在回复…"; lastPaint = Environment.TickCount64; Revision++;
            }
        }
        if (!pending.IsCompleted) return;
        try
        {
            Reply reply = pending.GetAwaiter().GetResult(); conversation.Restore(reply.State); SetAnswer(reply.Answer);
            Status = conversation.WasTrimmed ? "回答完成 · 早期对话已整理为摘要" : "回答完成 · 可以继续追问";
            if (reply.SaveFailed) Display.Add(new("system", "回答已收到，但保存对话失败。当前记忆仍在内存中，请检查 Mod 文件夹是否可写；下次提问会再次尝试保存。"));
        }
        catch (OperationCanceledException)
        {
            Display.Add(new("system", "本次请求已取消。"));
            if (Draft.Text.Length == 0) Draft.Replace(question); Status = "已取消 · 可编辑问题后重试";
        }
        catch (Exception error)
        {
            Display.Add(new("system", error.Message));
            if (Draft.Text.Length == 0) Draft.Replace(question); Status = "请求失败 · 原有对话未删除，可重试";
        }
        finally
        {
            pending = null; chunks = null; analyzingScreen = false; cancellation?.Dispose(); cancellation = null; Revision++;
        }
    }
    private void SetAnswer(string text)
    {
        if (answerIndex < 0) { answerIndex = Display.Count; Display.Add(new("assistant", text)); }
        else Display[answerIndex] = new("assistant", text);
    }
    private void Notice(string text, bool clearDraft)
    {
        if (clearDraft) Draft.Clear();
        Display.Add(new("system", text)); Revision++;
    }
    internal void Cancel() { cancellation?.Cancel(); if (pending != null) Status = "正在取消…"; }
    private void StopWork()
    {
        lock (bindingGate) generation++;
        cancellation?.Cancel();
        if (pending != null) _ = pending.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        pending = null; loading = null; resetSaving = null; resetNotice = false; chunks = null; analyzingScreen = false; cancellation?.Dispose(); cancellation = null;
    }
    private void ClearMemory()
    {
        Display.Clear(); conversation.Clear(); Draft.Clear(); answerIndex = -1; storageBlocked = false;
        Status = "新会话 · 当前存档的对话会自动保存";
    }
    internal void Reset(bool showConfirmation = false)
    {
        StopWork(); ClearMemory(); resetNotice = showConfirmation;
        lock (bindingGate) { if (store != null && saveKey != null) resetSaving = store.ClearAsync(saveKey); }
        if (resetSaving == null && showConfirmation) Display.Add(new("system", "已清除当前对话。"));
        Revision++;
    }
}
