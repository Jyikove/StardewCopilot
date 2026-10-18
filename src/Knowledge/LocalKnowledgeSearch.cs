namespace StardewCodexChat.Knowledge;

internal static class LocalKnowledgeSearch
{
    internal static List<Dictionary<string,string>> Search(KnowledgeDatabase db,string terms,bool wikiOnly=true)
    {
        var tokens=terms.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().Take(24).ToArray();if(tokens.Length==0)return new();
        string query=string.Join(" OR ",tokens.Select(t=>"\""+t.Replace("\"","")+"\""));
        var rows=db.Query("SELECT p.SourceName,p.Title,p.CanonicalUrl,p.FetchedAt,p.ExpiresAt,c.HeadingPath,c.Content,bm25(ChunkSearch,10,5,1) AS Rank FROM ChunkSearch JOIN Chunks c ON c.Id=ChunkSearch.rowid JOIN Pages p ON p.Id=c.PageId WHERE ChunkSearch MATCH ? AND (CAST(? AS INTEGER)=0 OR p.Domain='stardewvalleywiki.com') ORDER BY Rank LIMIT 64",query,wikiOnly?1:0);
        if(KnowledgeIntent.SeasonalFish(terms))
        {
            string season=tokens.First(KnowledgeIntent.Seasons.Contains);
            rows=rows.Where(d=>d["Title"].Contains(season,StringComparison.OrdinalIgnoreCase) || KnowledgeText.Tokens(d["Content"]).Split(' ').Contains(season)).ToList();
            return rows.OrderByDescending(d=>KnowledgeIntent.FishTable(d["Content"]) && d["HeadingPath"].Contains("Fish",StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(d=>KnowledgeIntent.FishTable(d["Content"])).Take(8).ToList();
        }
        var meaningful=tokens.Where(t=>t.All(c=>c<128)).ToArray();
        if(meaningful.Length>0)
        {
            var titleMatches=rows.Where(d=>meaningful.Any(t=>KnowledgeText.Tokens(d["Title"]).Split(' ').Contains(t))).ToList();
            if(titleMatches.Count>0)return titleMatches;
        }
        return rows;
    }
    internal static string Quality(List<Dictionary<string,string>> docs,string terms,double threshold)
    {
        if(docs.Count==0)return "Low";
        var wanted=terms.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t.All(c=>c<128)).Distinct().ToArray();
        if(wanted.Length==0)wanted=terms.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(KnowledgeIntent.SeasonalFish(terms))
        {
            string season=terms.Split(' ').First(KnowledgeIntent.Seasons.Contains);
            var tables=docs.Where(d=>KnowledgeIntent.FishTable(d["Content"]) && (d["Title"].Contains(season,StringComparison.OrdinalIgnoreCase)||d["Content"].Contains(season,StringComparison.OrdinalIgnoreCase))).ToList();
            if(tables.Count==0)return "Low";
            // Do not confuse a single fish/item fact with a seasonal list.
            int records=tables.Sum(d=>System.Text.RegularExpressions.Regex.Matches(d["Content"],@"(?:^|\n)Name=",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
            return records>=4?"High":"Medium";
        }
        string all=KnowledgeText.Tokens(string.Join(" ",docs.Select(d=>d["Title"]+" "+d["HeadingPath"]+" "+d["Content"])));
        bool subjectMatch=docs.Any(d=>wanted.Any(t=>KnowledgeText.Tokens(d["Title"]+" "+d["HeadingPath"]).Split(' ').Contains(t)));
        double coverage=wanted.Length==0?0:wanted.Count(t=>all.Split(' ').Contains(t))/(double)wanted.Length;
        bool gifting=wanted.Contains("gifts") || wanted.Contains("love");
        if(gifting && !docs.Any(d=>d["HeadingPath"].Contains("Gift",StringComparison.OrdinalIgnoreCase)))return "Medium";
        return subjectMatch && coverage>=Math.Clamp(threshold,.5,1)?"High":coverage>.2?"Medium":"Low";
    }
}
