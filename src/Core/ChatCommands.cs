namespace StardewCodexChat.Core;

public enum ChatCommandKind { None, ClearChat, ClearTodo, ResetKey, Help, Search, Screen, Invalid }
public readonly record struct ChatCommand(ChatCommandKind Kind, string Argument);

public static class ChatCommands
{
    public static ChatCommand Parse(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('/')) return new(ChatCommandKind.None, "");
        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        string name = text[..end].ToLowerInvariant();
        string argument = text[end..].Trim();
        return name switch
        {
            "/clearchat" when argument.Length == 0 => new(ChatCommandKind.ClearChat, ""),
            "/cleartodo" when argument.Length == 0 => new(ChatCommandKind.ClearTodo, ""),
            "/resetkey" when argument.Length == 0 => new(ChatCommandKind.ResetKey, ""),
            "/help" => new(ChatCommandKind.Help, Unquote(argument)),
            "/search" => new(ChatCommandKind.Search, Unquote(argument)),
            "/screen" => new(ChatCommandKind.Screen, Unquote(argument)),
            _ => new(ChatCommandKind.Invalid, "")
        };
    }
    private static string Unquote(string text)
    {
        if (text.StartsWith('+')) text = text[1..].Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '“' && text[^1] == '”')
            || (text[0] == '\'' && text[^1] == '\''))) return text[1..^1].Trim();
        return text;
    }
}
