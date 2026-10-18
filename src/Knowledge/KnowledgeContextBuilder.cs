namespace StardewCodexChat.Knowledge;

internal static class KnowledgeContextBuilder
{
    internal static string Body(Dictionary<string,string> doc,int max=2000)
    {
        string text=doc["Content"];
        if(KnowledgeIntent.FishTable(text))
        {
            // Preserve each complete record's conditions instead of spending context on price-profession sub-tables.
            var allowed=new[]{"Name=","Location=","Time=","Season=","Weather=","Price="};
            text=string.Join("\n",text.Split('\n').Select(line=>line.Contains("Name=",StringComparison.OrdinalIgnoreCase)
                ?string.Join(" | ",line.Split(" | ").Where(field=>allowed.Any(p=>field.StartsWith(p,StringComparison.OrdinalIgnoreCase)))):line));
            if(text.Length>max)
            {
                int cut=text.LastIndexOf('\n',max-100,Math.Min(max-100,text.Length));
                if(cut>0)return text[..cut]+"\n[Additional table rows omitted; this is not the complete list.]";
            }
        }
        return text.Length<=max?text:text[..(max-60)]+" [Excerpt truncated; query missing details separately.]";
    }
}
