using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace StardewCodexChat.Core;

public sealed record TodoItem(string Text, bool Completed = false, long CompletedAtTicks = 0);

/// <summary>A small per-save list stored only inside the mod's data directory.</summary>
public sealed class TodoList
{
    public const int MaxItems = 200;
    public const int MaxTextLength = 300;
    private readonly string path;
    private bool loadFailed;
    private List<TodoItem> items = new();
    public IReadOnlyList<TodoItem> Items => items;
    public IEnumerable<int> VisibleIndices => Enumerable.Range(Math.Max(0, items.Count - 30), Math.Min(30, items.Count))
        .Reverse().OrderBy(i => items[i].Completed).ThenByDescending(i => items[i].CompletedAtTicks);
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public TextBuffer Draft { get; } = new();
    public int Revision { get; private set; }
    public string Error { get; private set; } = "";

    public TodoList(string directory, string saveKey)
    {
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saveKey))).ToLowerInvariant();
        path = Path.Combine(directory, name + ".json");
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException();
            var loaded = JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(path));
            if (loaded == null || loaded.Count > MaxItems || loaded.Any(x => x == null || string.IsNullOrWhiteSpace(x.Text) || x.Text.Length > MaxTextLength || x.CompletedAtTicks < 0 || x.CompletedAtTicks > DateTime.MaxValue.Ticks))
                throw new InvalidDataException();
            items = loaded;
        }
        catch
        {
            loadFailed = true;
            Error = "待办读取失败，原文件已保留。";
        }
    }
    public bool Add()
    {
        if (!AddText(Draft.Text)) return false;
        Draft.Clear(); return true;
    }
    private bool AddText(string text)
    {
        if (loadFailed) return false;
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text.Length > MaxTextLength) { Error = $"每条待办最多 {MaxTextLength} 个字符。"; return false; }
        if (items.Count >= MaxItems) { Error = $"最多保存 {MaxItems} 条待办。"; return false; }
        var next = new List<TodoItem>(items) { new(text) };
        return Save(next);
    }
    public bool Clear()
    {
        if (!Save(new List<TodoItem>())) return false;
        loadFailed = false; Draft.Clear(); return true;
    }
    public bool Toggle(int index)
    {
        if (loadFailed || index < 0 || index >= items.Count) return false;
        var next = new List<TodoItem>(items);
        bool completed = !next[index].Completed;
        long stamp = completed ? Math.Max(DateTime.UtcNow.Ticks, items.Max(item => item.CompletedAtTicks) + 1) : 0;
        next[index] = next[index] with { Completed = completed, CompletedAtTicks = stamp };
        return Save(next);
    }
    // Executed by the existing main-thread dispatcher, against the currently bound save.
    public string ExecuteTool(string name, string arguments)
    {
        if (!GameTools.TryArguments(name, arguments, out string value)) return "{\"error\":\"Invalid todo tool arguments.\"}";
        if (loadFailed) return JsonSerializer.Serialize(new { error = Error }, JsonOptions);
        if (name == "add_todo")
        {
            if (items.Any(item => !item.Completed && string.Equals(item.Text, value, StringComparison.OrdinalIgnoreCase)))
                return JsonSerializer.Serialize(new { added = false, alreadyExists = true, text = value, completed = false }, JsonOptions);
            bool added = AddText(value);
            return added ? JsonSerializer.Serialize(new { added = true, text = value, completed = false }, JsonOptions)
                : JsonSerializer.Serialize(new { error = Error.Length > 0 ? Error : "Todo text is empty." }, JsonOptions);
        }
        if (name != "get_todos") return "{\"error\":\"Unsupported todo tool.\"}";
        var matches = Enumerable.Range(0, items.Count).Reverse()
            .Where(i => value.Length == 0 || items[i].Text.Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
        var result = new List<TodoItem>();
        foreach (int index in matches.Take(30))
        {
            result.Add(items[index]);
            if (JsonSerializer.Serialize(result, JsonOptions).Length > 8500) { result.RemoveAt(result.Count - 1); break; }
        }
        return JsonSerializer.Serialize(new { total = items.Count, matched = matches.Length,
            truncated = result.Count < matches.Length, items = result }, JsonOptions);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int chmod(string path, uint mode);

    private bool Save(List<TodoItem> next)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows() && chmod(temporary, 0x180) != 0) throw new IOException();
                JsonSerializer.Serialize(file, next);
                file.Flush(true);
            }
            File.Move(temporary, path, true);
            items = next; Error = ""; Revision++; return true;
        }
        catch { Error = "待办保存失败，请检查 Mod 文件夹是否可写。"; return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}
