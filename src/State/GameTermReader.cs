using StardewValley;
using StardewCodexChat.Core;

namespace StardewCodexChat.State;

/// <summary>Only invoked on the game thread; reads public definitions, never save ownership.</summary>
internal static class GameTermReader
{
    internal static IEnumerable<GameTerm> Read()
    {
        var terms = GameTermSources.Read((asset, chinese) => Game1.content.Load<Dictionary<string, string>>(asset,
            chinese ? LocalizedContentManager.LanguageCode.zh : LocalizedContentManager.LanguageCode.en)).ToList();
        if (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh)
        {
            foreach (string type in new[] { "(O)", "(BC)", "(W)", "(B)", "(H)", "(T)", "(TR)", "(F)", "(P)", "(S)" })
            {
                try
                {
                    var definition = ItemRegistry.GetTypeDefinition(type);
                    if (definition == null) continue;
                    foreach (string id in definition.GetAllIds())
                    {
                        var data = ItemRegistry.GetData(ItemRegistry.IsQualifiedItemId(id) ? id : type + id);
                        if (data != null && !data.IsErrorItem) terms.Add(new(data.InternalName, data.DisplayName));
                    }
                }
                catch { /* A custom item type cannot block the remaining game translations. */ }
            }
        }
        return terms;
    }
}
