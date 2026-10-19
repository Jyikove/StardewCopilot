using System.Text.Json;
using StardewCodexChat.Core;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

string root = Path.Combine(Path.GetTempPath(), "stardew-copilot-mod-test-" + Guid.NewGuid().ToString("N"));
string modPath = Path.Combine(root, "LookupAnything");
Directory.CreateDirectory(modPath);
try
{
    await File.WriteAllTextAsync(Path.Combine(modPath, "config.json"), """
    {
      "LookupKey": "F6",
      "SearchKey": "LeftShift + F6",
      "DisplayMode": "Compact",
      "ApiKey": "must-never-leak",
      "AccessToken": "must-never-leak-either",
      "Nested": { "Theme": "Blue", "clientSecret": "nested-secret" }
    }
    """);

    string capturedQuery = "";
    var environment = new ModEnvironment((query, _) =>
    {
        capturedQuery = query;
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            quality = "High",
            cacheOnly = false,
            chunks = new[]
            {
                new
                {
                    title = "Lookup Anything",
                    heading = "Hotkeys",
                    source = "Community",
                    authority = "community",
                    url = "https://www.nexusmods.com/stardewvalley/mods/541",
                    stale = false,
                    body = "Lookup Anything lets players inspect objects and NPCs using its configured lookup key."
                },
                new
                {
                    title = "Fishing guide",
                    heading = "Summer",
                    source = "Game Wiki",
                    authority = "wiki",
                    url = "https://stardewvalleywiki.com/Fish",
                    stale = false,
                    body = "Summer fish are available in several locations."
                }
            }
        }));
    });

    environment.ReplaceMods(new[]
    {
        new ModDescriptor("Pathoschild.LookupAnything", "Lookup Anything", "1.45.0", true, modPath, "Pathoschild", "Inspect game objects", "LookupAnything.dll"),
        new ModDescriptor("Fixture.OtherMod", "Other Mod", "2.0.0", false, Path.Combine(root, "OtherMod"), "Fixture", "Fixture", "Other.dll")
    });

    string inventory = environment.InventoryJson;
    Check(inventory.Contains("Pathoschild.LookupAnything") && inventory.Contains("\"loaded\":true"), "inventory exposes compact Mod IDs and loaded status");
    Check(!inventory.Contains(modPath) && !inventory.Contains("Inspect game objects"), "inventory never exposes local paths or full descriptions");
    Check(environment.IsLikelyModQuestion("How do I use Lookup Anything?"), "Mod name routes a question to Mod-aware tools");
    Check(environment.IsLikelyModQuestion("怎么改快捷键？"), "shortcut/config language can trigger Mod-aware tools");
    Check(!environment.IsLikelyModQuestion("夏天种什么最赚钱？"), "ordinary game questions are not routed as Mod questions");

    string details = await environment.ExecuteAsync("get_mod_details", """{"unique_id":"Pathoschild.LookupAnything"}""", default);
    using (var json = JsonDocument.Parse(details))
    {
        Check(json.RootElement.GetProperty("version").GetString() == "1.45.0", "details return the local manifest version");
        Check(json.RootElement.GetProperty("config_file_exists").GetBoolean(), "details report whether root config.json exists");
        Check(!details.Contains(modPath), "details do not reveal local paths");
    }

    string namesOnly = await environment.ExecuteAsync("get_mod_config", """{"unique_id":"Pathoschild.LookupAnything"}""", default);
    using (var json = JsonDocument.Parse(namesOnly))
    {
        Check(json.RootElement.GetProperty("available_fields").ToString().Contains("LookupKey"), "omitting fields lists safe setting names");
        Check(!namesOnly.Contains("F6") && !namesOnly.Contains("must-never-leak"), "field discovery does not return values or secrets");
    }

    string config = await environment.ExecuteAsync("get_mod_config", """
    {"unique_id":"Pathoschild.LookupAnything","fields":["LookupKey","SearchKey","DisplayMode","ApiKey","AccessToken","Nested"]}
    """, default);
    using (var json = JsonDocument.Parse(config))
    {
        var settings = json.RootElement.GetProperty("settings");
        Check(settings.GetProperty("LookupKey").GetString() == "F6", "requested custom shortcut is read from the current local config");
        Check(settings.GetProperty("SearchKey").GetString() == "LeftShift + F6", "multiple selected config fields are returned");
        Check(settings.GetProperty("Nested").GetProperty("Theme").GetString() == "Blue", "safe nested settings remain readable");
        Check(!settings.GetProperty("Nested").ToString().Contains("clientSecret"), "sensitive nested fields are filtered");
        Check(json.RootElement.GetProperty("omitted_sensitive_fields").GetArrayLength() == 2, "sensitive requested fields are explicitly omitted");
        Check(json.RootElement.GetProperty("runtime_status").GetString() == "unverified", "saved settings are not misrepresented as runtime-verified");
    }
    Check(!config.Contains("must-never-leak") && !config.Contains("nested-secret"), "secret values never leave the local config reader");

    string pathAttack = await environment.ExecuteAsync("get_mod_config", """
    {"unique_id":"../../secrets","fields":["LookupKey"],"path":"/etc/passwd"}
    """, default);
    Check(pathAttack.Contains("error") && !pathAttack.Contains("root:"), "unknown IDs and arbitrary paths are rejected");

    string docs = await environment.ExecuteAsync("search_mod_docs", """
    {"unique_id":"Pathoschild.LookupAnything","query":"shortcut configuration"}
    """, default);
    using (var json = JsonDocument.Parse(docs))
    {
        Check(json.RootElement.GetProperty("chunks").GetArrayLength() == 1, "documentation search filters unrelated game Wiki pages");
        Check(json.RootElement.GetProperty("chunks")[0].GetProperty("url").GetString() == "https://www.nexusmods.com/stardewvalley/mods/541", "documentation returns a real normalized source URL");
    }
    Check(capturedQuery.Contains("Lookup Anything") && capturedQuery.Contains("Pathoschild.LookupAnything")
        && capturedQuery.Contains("shortcut configuration"), "documentation query is scoped to the selected Mod");
    string unknown = await environment.ExecuteAsync("get_mod_details", """{"unique_id":"Unknown.Mod"}""", default);
    Check(unknown.Contains("error"), "unknown Mod IDs cannot access local files");

    Console.WriteLine($"All {passed} Mod tool checks passed. Temporary synthetic data only; no network or real configuration used.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
