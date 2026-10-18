using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StardewCodexChat.Core;

/// <summary>Per-question convergence only; never stores evidence on disk.</summary>
public sealed class ResearchBudget
{
    public const int TotalSeconds=55, LookupSeconds=25, MaxSearches=4, MaxGameQueries=12, MaxToolRounds=3;
    private readonly Func<double> seconds;
    private readonly double lookupDeadline;
    private readonly HashSet<string> queries=new(StringComparer.Ordinal);
    private readonly HashSet<string> evidence=new(StringComparer.Ordinal);
    private readonly Dictionary<string,int> gameReads=new(StringComparer.Ordinal);
    private int noNewEvidence;
    public int Searches {get;private set;}
    public int GameQueries {get;private set;}
    public string StopReason {get;private set;}="";
    public double LookupRemaining=>Math.Max(0,lookupDeadline-seconds());
    public ResearchBudget(Func<double>? elapsedSeconds=null,int totalSeconds=TotalSeconds)
    {
        var timer=Stopwatch.StartNew();seconds=elapsedSeconds??(()=>timer.Elapsed.TotalSeconds);
        lookupDeadline=Math.Min(LookupSeconds,totalSeconds/2.0);
    }
    public void Finish(string reason){if(StopReason.Length==0)StopReason=reason;}
    public bool ShouldFinalize(long round)
    {
        if(LookupRemaining<=0)Finish("lookup_deadline");
        if(round>=MaxToolRounds)Finish("round_budget");
        if(Searches>=MaxSearches)Finish("search_budget");
        if(GameQueries>=MaxGameQueries)Finish("game_query_budget");
        return StopReason.Length>0;
    }
    public bool TrySearch(string query)
    {
        if(ShouldFinalize(0))return false;
        string key=Regex.Replace(query.ToLowerInvariant(),@"[\p{P}\p{Z}\s]+"," ").Trim();
        if(!queries.Add(key)){Finish("duplicate_query");return false;}
        Searches++;return true;
    }
    public bool TryGameQuery(string name,string arguments)
    {
        if(ShouldFinalize(0))return false;
        string key=name+arguments;
        int count=gameReads.GetValueOrDefault(key);
        if(count>=2){Finish("repeated_game_query");return false;}
        gameReads[key]=count+1;GameQueries++;return true;
    }
    public void ObserveEvidence(JsonElement result)
    {
        int added=0;
        if(result.TryGetProperty("chunks",out var chunks))foreach(var chunk in chunks.EnumerateArray())
        {
            if(!chunk.TryGetProperty("body",out var body)||string.IsNullOrWhiteSpace(body.GetString()))continue;
            string key=chunk.GetProperty("url").GetString()+"\n"+body.GetString();
            if(evidence.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))))added++;
        }
        noNewEvidence=added>0?0:noNewEvidence+1;
        if(noNewEvidence>=2)Finish("no_new_evidence");
    }
    public string Guidance=> $"本次深度检索预算：还可联网{Math.Max(0,MaxSearches-Searches)}次、查询游戏状态{Math.Max(0,MaxGameQueries-GameQueries)}次，检索阶段剩余约{Math.Ceiling(LookupRemaining)}秒。只解决当前问题的必要缺口，不扩大到无关攻略。资料足够立即回答；未覆盖的次要细节标明不确定，不追求穷尽所有机制。";
}
