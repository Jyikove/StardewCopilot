using System.Text.Json;
using StardewCodexChat.Core;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "stardew-copilot-mod-test-" + Guid.NewGuid().ToString("N"));
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

    async Task<string> Read(string fields="[\"LookupKey\"]") => await environment.ExecuteAsync("get_mod_config", "{\"unique_id\":\"Pathoschild.LookupAnything\",\"fields\":"+fields+"}",default);
    string configPath=Path.Combine(modPath,"config.json");
    await File.WriteAllTextAsync(configPath,"{ // SMAPI-style comment\n\"LookupKey\":\"F8\", }");
    Check((await Read()).Contains("F8"),"config supports comments/trailing commas and reads updated values");
    foreach(string bad in new[]{"{broken", "[]", new string(' ',ModEnvironment.MaxConfigBytes+1), new string('[',20)+new string(']',20)})
    {await File.WriteAllTextAsync(configPath,bad);Check((await Read()).Contains("error"),"malformed, deep or oversized configs return bounded errors");}
    await File.WriteAllTextAsync(configPath,"{\"DiscordToken\":\"not-public\",\"密码\":\"not-public\",\"Endpoint\":\"https://example.com/?api_key=not-public\",\"Folder\":\"/private/local-folder\"}");
    string protectedValues=await Read("[\"DiscordToken\",\"密码\",\"Endpoint\",\"Folder\"]");
    Check(!protectedValues.Contains("not-public")&&!protectedValues.Contains("/private/local-folder"),"additional tokens, Chinese credentials, URL credentials and absolute paths are withheld");
    File.Delete(configPath);
    string external=Path.Combine(root,"outside.json");await File.WriteAllTextAsync(external,"{\"LookupKey\":\"EXTERNAL\"}");
    File.CreateSymbolicLink(configPath,external);
    Check(!(await Read()).Contains("EXTERNAL")&&(await Read()).Contains("error"),"config file symlinks cannot escape the Mod boundary");
    File.Delete(configPath);
    string linked=Path.Combine(root,"linked-mod");Directory.CreateSymbolicLink(linked,modPath);
    await File.WriteAllTextAsync(configPath,"{\"LookupKey\":\"EXTERNAL\"}");
    environment.ReplaceMods(new[]{new ModDescriptor("Pathoschild.LookupAnything","Lookup Anything","1",true,linked)});
    Check((await Read()).Contains("error"),"symlink Mod directories are also rejected");
    environment.ReplaceMods(new[]{new ModDescriptor("Pathoschild.LookupAnything","Lookup Anything","1",true,"")});
    Check(environment.InventoryJson.Contains("Pathoschild.LookupAnything")&&(await Read()).Contains("error"),"unknown paths preserve manifest discovery without granting config access");
    await File.WriteAllTextAsync(Path.Combine(modPath,"manifest.json"),"{\"UniqueID\":\"Pathoschild.LookupAnything\"}");
    Check(ModDirectoryIndex.Discover(root).GetValueOrDefault("Pathoschild.LookupAnything")==modPath,"public manifest discovery resolves directories without internal SMAPI APIs or following links");
    string duplicate=Path.Combine(root,"duplicate");Directory.CreateDirectory(duplicate);
    await File.WriteAllTextAsync(Path.Combine(duplicate,"manifest.json"),"{\"UniqueID\":\"Pathoschild.LookupAnything\"}");
    Check(!ModDirectoryIndex.Discover(root).ContainsKey("Pathoschild.LookupAnything"),"ambiguous duplicate IDs cannot grant config access to the wrong folder");
    using var canceled=new CancellationTokenSource();canceled.Cancel();
    bool cancellationObserved=false;try{await environment.ExecuteAsync("get_mod_config","{}",canceled.Token);}catch(OperationCanceledException){cancellationObserved=true;}
    Check(cancellationObserved,"cancellation is propagated rather than converted to a successful tool result");
    Console.WriteLine($"All {passed} Mod tool checks passed. Temporary synthetic data only; no network or real configuration used.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
