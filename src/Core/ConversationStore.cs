using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace StardewCodexChat.Core;

public sealed record ConversationState(int Version, string Summary, ChatMessage[] Recent, ChatMessage[] Transcript, int Compressions)
{
    public static ConversationState Empty => new(1, "", Array.Empty<ChatMessage>(), Array.Empty<ChatMessage>(), 0);
    public void Validate()
    {
        if (Version != 1 || Summary == null || Summary.Length > Conversation.MaxSummaryLength || Recent == null || Transcript == null
            || Compressions < 0 || Recent.Length > 26 || Recent.Length > Transcript.Length || Recent.Length % 2 != 0 || Transcript.Length % 2 != 0)
            throw new InvalidDataException("Invalid conversation file.");
        foreach (var list in new[] { Recent, Transcript })
            for (int i = 0; i < list.Length; i++)
                if (list[i] == null || list[i].Role != (i % 2 == 0 ? "user" : "assistant") || string.IsNullOrWhiteSpace(list[i].Text)
                    || list[i].Text.Length > (i % 2 == 0 ? Conversation.MaxInputLength
                        : ChatCommands.Parse(list[i-1].Text).Kind==ChatCommandKind.Search ? DeepSeekClient.MaxSearchAnswerLength : DeepSeekClient.MaxAnswerLength))
                    throw new InvalidDataException("Invalid conversation messages.");
        if (!Recent.SequenceEqual(Transcript.Skip(Transcript.Length - Recent.Length))) throw new InvalidDataException("Conversation tail mismatch.");
    }
}
public sealed record LoadedConversation(ConversationState State, bool RecoveredBackup);

/// <summary>All operations are queued in order. Files stay inside the supplied mod data directory.</summary>
public sealed class ConversationStore
{
    private const long MaxFileBytes = 64L * 1024 * 1024;
    private readonly string directory;
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public ConversationStore(string directory) { this.directory = Path.GetFullPath(directory); }
    public string PathFor(string saveKey) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saveKey))).ToLowerInvariant() + ".json");
    private Task<T> Queue<T>(Func<Task<T>> work)
    {
        lock (gate)
        {
            Task previous = tail;
            var task = Task.Run(async () => { try { await previous; } catch { } return await work(); });
            tail = task;
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }
    }
    public Task<LoadedConversation> LoadAsync(string saveKey) => Queue(async () => {
        string path = PathFor(saveKey);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new LoadedConversation(ConversationState.Empty, false);
        try { return new LoadedConversation(await Read(path), false); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            try { return new LoadedConversation(await Read(path + ".bak"), true); }
            catch { throw new IOException("无法恢复此存档的对话记录。原文件已保留，请检查 Mod 内 data/conversations 文件夹；不会自动覆盖原记录。"); }
        }
    });
    public Task SaveAsync(string saveKey, ConversationState state) => Queue(() => WriteState(saveKey, state, true));
    public Task ClearAsync(string saveKey) => Queue(() => WriteState(saveKey, ConversationState.Empty, false));
    private async Task<bool> WriteState(string saveKey, ConversationState state, bool keepBackup)
    {
        state.Validate();
        Directory.CreateDirectory(directory);
        string path = PathFor(saveKey), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                if (OperatingSystem.IsMacOS()) SetPrivate(temporary);
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
                if (stream.Length > MaxFileBytes) throw new IOException("对话记录文件超过保存上限，旧文件仍保留。");
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            bool validPrevious = false;
            if (keepBackup && File.Exists(path))
                try { await Read(path); validPrevious = true; } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            if (validPrevious) File.Move(path, path + ".bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
            if (!keepBackup)
            {
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                foreach (string stale in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.tmp")) File.Delete(stale);
            }
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<ConversationState> Read(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("Conversation file too large.");
        var state = await JsonSerializer.DeserializeAsync<ConversationState>(stream) ?? throw new InvalidDataException("Empty conversation file.");
        state.Validate(); return state;
    }
    public void FlushOnExit()
    {
        Task pending; lock (gate) pending = tail;
        try { pending.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int chmod(string path, uint mode);
    private static void SetPrivate(string path)
    {
        if (chmod(path, 0x180) != 0) throw new IOException("无法设置对话文件权限。"); // 0600
    }
}
