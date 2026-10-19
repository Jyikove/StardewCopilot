namespace StardewCodexChat.Core;

/// <summary>Pair game-owned locale entries by stable asset key, never by model translation.</summary>
public static class GameTermSources
{
    public static IEnumerable<GameTerm> Read(Func<string, bool, Dictionary<string, string>> load)
    {
        foreach (string asset in new[] { "Weapons", "Objects", "BigCraftables", "Tools", "Buildings", "Locations", "EnchantmentNames", "NPCNames", "UI", "StringsFromCSFiles" })
        {
            Dictionary<string, string> en, zh;
            try { en = load("Strings/" + asset, false); zh = load("Strings/" + asset, true); }
            catch { continue; } // Optional assets or incomplete translations do not break chat.
            foreach (var row in en)
            {
                bool include = asset is "EnchantmentNames" or "NPCNames"
                    || row.Key.EndsWith("_Name", StringComparison.OrdinalIgnoreCase)
                    || asset == "UI" && System.Text.RegularExpressions.Regex.IsMatch(row.Key,
                        @"^ItemHover_(?:.*Bonus|Damage|Speed|Weight|Energy|Health|Buff[0-9]+)$")
                    || asset == "StringsFromCSFiles" && (row.Key.StartsWith("Buff.cs.", StringComparison.Ordinal) || row.Key.EndsWith("_BuffName", StringComparison.Ordinal));
                if (include && zh.TryGetValue(row.Key, out var translated))
                    yield return new GameTerm(Label(row.Value), Label(translated));
            }
        }
    }
    private static string Label(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\{[0-9]+(?::[^}]*)?\}", "")
        .Trim(' ', '+', '-', '%', ':', '：');
}
