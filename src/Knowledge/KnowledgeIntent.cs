namespace StardewCodexChat.Knowledge;

/// <summary>Query constraints and navigation hints, never hard-coded game answers.</summary>
internal static class KnowledgeIntent
{
    internal static readonly string[] Seasons={"spring","summer","fall","winter"};
    internal static bool SeasonalFish(string terms)=>!terms.Split(' ').Contains("smoker")&&terms.Split(' ').Any(Seasons.Contains)&&terms.Split(' ').Any(t=>t is "fish" or "fishing");
    internal static string? ReferencePage(string query)
    {
        string terms=KnowledgeText.QueryTerms(query);if(!SeasonalFish(terms))return null;
        string season=terms.Split(' ').First(Seasons.Contains);
        return "https://stardewvalleywiki.com/"+char.ToUpperInvariant(season[0])+season[1..];
    }
    internal static bool FishTable(string content)=>content.Contains("Location=",StringComparison.OrdinalIgnoreCase)&&content.Contains("Time=",StringComparison.OrdinalIgnoreCase)&&content.Contains("Weather=",StringComparison.OrdinalIgnoreCase);
}
