using System.Text.RegularExpressions;

namespace StardewCodexChat.Core;

public sealed record CitationPart(string Text,string? Url=null);

/// <summary>Inline source syntax: [https://one https://two]. Numbering is local to each answer.</summary>
public static class CitationMarkup
{
    private static readonly Regex Markers=new(@"\[(?<group>\s*https://[^\[\]\r\n]+)\]|\[[^\[\]\r\n]*\]\((?<markdown>https://[^\s<>\[\]]+)\)|(?<bare>https://[^\s\[\]<>，。；！？、]+)",RegexOptions.Compiled);
    public static string Strip(string text)=>string.Concat(Parse(text).Where(p=>p.Url==null).Select(p=>p.Text)).Trim();
    private static string TrimUrl(string value)
    {
        value=value.TrimEnd('.',',',';','，','。','；');
        while(value.EndsWith(')')&&value.Count(c=>c==')')>value.Count(c=>c=='('))value=value[..^1];
        return value;
    }
    public static IReadOnlyList<CitationPart> Parse(string text)
    {
        text=Regex.Replace(text,@"http://(?:www\.)?stardewvalleywiki\.com/","https://stardewvalleywiki.com/",RegexOptions.IgnoreCase);
        var parts=new List<CitationPart>();var numbers=new Dictionary<string,int>(StringComparer.Ordinal);
        // Compatibility with older stored plain source lines; no history files are rewritten.
        text=Regex.Replace(text,@"\s*来源[：:]\s*(?=https://|\[https://)","");
        text=Regex.Replace(text,@"[\r\n]+\s*(?=\[\s*https://)","");
        int cursor=0;
        void Plain(string value){if(value.Length>0)parts.Add(new(value));}
        foreach(Match match in Markers.Matches(text))
        {
            Plain(text[cursor..match.Index]);cursor=match.Index+match.Length;
            string value=match.Groups["group"].Success?match.Groups["group"].Value:match.Groups["markdown"].Success?match.Groups["markdown"].Value:match.Groups["bare"].Value;
            string[] candidates=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            var urls=new List<string>();
            foreach(string candidate in candidates)
            {
                string raw=TrimUrl(candidate);
                if(!PublicWebUrl.TryNormalize(raw,out string url)){urls.Clear();break;}
                urls.Add(url);
            }
            if(urls.Count==0){Plain(match.Value);continue;}
            foreach(string url in urls)
            {
                if(!numbers.TryGetValue(url,out int number))numbers[url]=number=numbers.Count+1;
                parts.Add(new("["+number+"]",url));
            }
            if(match.Groups["bare"].Success)
            {
                string raw=match.Groups["bare"].Value;string trimmed=TrimUrl(raw);Plain(raw[trimmed.Length..]);
            }
        }
        Plain(text[cursor..]);return parts;
    }
}
