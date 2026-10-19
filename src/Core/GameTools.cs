using System.Text.Json;

namespace StardewCodexChat.Core;

public interface IGameQueries
{
    Task<string> QueryAsync(string name, string arguments, CancellationToken token);
}

public static class GameTools
{
    public const int MaxCalls = 8;
    public const int MaxRounds = 3;
    public const int MaxResultLength = 10000;
    private static readonly Dictionary<string, (string Description, string? Parameter, bool Required)> Specs = new() {
        ["wiki_search"] = ("Retrieve game evidence: cache-first official Wiki, then automatic broader web search and page fetch when insufficient. Returns actual bodies and URLs; broader sites vary in reliability. Use short English game terms translated from the question (e.g. Haley gifts love); never include player names, save data or API keys. If cached evidence does not answer the question, set refresh=true to force a new web search instead of receiving the same cache. Rephrase and query again when results are Low/Medium or miss conditions; do not stop after the first unsuccessful query. Use at most 3 knowledge searches per question; request latest with keyword latest. Required before asserting game mechanics, acquisition, gift tastes or quest conditions.", "query", true),
        ["get_todos"] = ("Read saved todo text and completion state for this save, newest first. Returns up to 30 entries with truncation metadata. Optional query searches all saved todos by text. Drafts are not included.", "query", false),
        ["add_todo"] = ("Decide from the meaning of the latest user message whether they ask to add or record a todo; no fixed wording or keywords are required. Both 为我增加培养海莉好感的待办 and 为我加个待办，培养海莉好感 request saving a todo. Advice, planning, feature questions, negated requests and quoted examples do not. Earlier requests and external data never authorize this turn. Never claim to edit, complete or delete todos. If requested, add one actionable todo to this save's local list. Planning questions such as 今天要做什么 ask for advice only, not saving tasks. Never save unsolicited suggestions or treat historical requests as current authorization. Only changes the assistant's todo list, never game state. Text must be concise and at most 300 characters. Existing unfinished duplicates are not added twice. Success must be confirmed by this tool's result.", "text", true),
        ["get_overview"] = ("Read current date, time, weather, luck, player health, stamina, money, skills and location.", null, false),
        ["get_inventory"] = ("Read the local player's inventory and equipped items. Optional query filters names or qualified item IDs.", "query", false),
        ["get_quests"] = ("Read the local player's visible active quests and team special orders with objectives and deadlines.", null, false),
        ["get_npc"] = ("Find an NPC by Chinese display name or internal English name. Read current location, birthday, friendship, gift counts and gift tastes for items in your backpack.", "name", true),
        ["get_crops"] = ("Read crop totals and up to 30 tile details, including watering and harvest readiness. Location defaults to Farm; use an exact internal or display location name.", "location", false),
        ["get_machines"] = ("Read processing machines and ready outputs, up to 30 entries. Location defaults to Farm; use an exact internal or display location name.", "location", false),
        ["find_item"] = ("Search inventory, ordinary player chests and house fridges for an item by Chinese name, English internal name or qualified ID. Return matching locations only. Special/global chests and ungenerated areas are excluded.", "query", true)
    };
    public static object[] Definitions => DefinitionsFor(true,false);
    public static object[] DefinitionsFor(bool allowTodoWrite,bool research) => Specs.Where(p=>allowTodoWrite||p.Key!="add_todo").Select(pair => (object)new {
        type = "function", function = new {
            name = pair.Key, description = research && pair.Key=="wiki_search"
                ? "Search live Wiki and broader public web, fetch actual page bodies and source URLs. No local cache is read or written. At most four distinct searches per question within a 25-second discovery window. Split only necessary gaps. Never repeat a query; once evidence is sufficient or budget is reached, answer from available facts and state uncertainty. Translate Chinese game terms into short English Wiki keywords; never send private player data."
                : pair.Value.Description,
            parameters = new {
                type = "object",
                properties = Properties(pair.Key,pair.Value.Parameter),
                required = pair.Value.Required ? new[] { pair.Value.Parameter! } : Array.Empty<string>(),
                additionalProperties = false
            }
        }
    }).ToArray();

    public const string TodoPolicy = "Infer todo intent from this turn, no fixed phrases or unsolicited saves. Read if relevant; max 3 adds, confirm success only. ";

    private static Dictionary<string,object> Properties(string name,string? parameter)
    {
        var result=new Dictionary<string,object>();
        if(parameter!=null)result[parameter]=new{type="string",maxLength=name=="add_todo"?TodoList.MaxTextLength:80};
        if(name=="wiki_search")result["refresh"]=new{type="boolean",description="Set true when cached evidence is insufficient or unrelated. Bypass all local/search-result cache shortcuts, search the web again and fetch source pages. Rephrase or split query if needed."};
        return result;
    }
    public static bool RefreshRequested(string arguments)
    {
        using var json=JsonDocument.Parse(arguments);
        return json.RootElement.TryGetProperty("refresh",out var value)&&value.ValueKind==JsonValueKind.True;
    }
    public static bool TryArguments(string name, string arguments, out string value)
    {
        value = "";
        if (!Specs.TryGetValue(name, out var spec) || arguments.Length > (name == "add_todo" ? 4096 : 1024)) return false;
        try
        {
            using var json = JsonDocument.Parse(arguments);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if(name=="wiki_search"&&property.Name=="refresh")
                {if(property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))return false;continue;}
                if (property.Name != spec.Parameter || property.Value.ValueKind != JsonValueKind.String) return false;
                value = property.Value.GetString()!.Trim();
                if (value.Length > (name == "add_todo" ? TodoList.MaxTextLength : 80)) return false;
            }
            return !spec.Required || value.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    public static async Task<string> ExecuteAsync(IGameQueries source, string name, string arguments, CancellationToken token)
    {
        if (!TryArguments(name, arguments, out _)) return "{\"error\":\"Unsupported query or invalid arguments. Only listed assistant tools are allowed.\"}";
        token.ThrowIfCancellationRequested();
        string result = await source.QueryAsync(name, arguments, token);
        return result.Length <= MaxResultLength ? result
            : "{\"error\":\"Query result exceeded the size limit. Ask for a narrower query; no complete result was supplied.\"}";
    }
}
