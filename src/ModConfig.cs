using StardewModdingAPI.Utilities;
using StardewCodexChat.Core;

namespace StardewCodexChat;

public sealed class ModConfig
{
    public KeybindList OpenChat { get; set; } = KeybindList.Parse("Enter");
    public KeybindList OpenChatMultiplayer { get; set; } = KeybindList.Parse("G");
    public string Model { get; set; } = "deepseek-flash";
    public Knowledge.KnowledgeOptions Knowledge { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 120;

    public int MemoryTokenBudget { get; set; } = Conversation.DefaultMemoryBudget;

    internal ChatOptions GetChatOptions() => new(Model, Math.Clamp(TimeoutSeconds, 10, 600), Math.Clamp(MemoryTokenBudget, 12000, 64000));
}
