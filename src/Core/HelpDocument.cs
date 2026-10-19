using System.Text.RegularExpressions;

namespace StardewCodexChat.Core;

public sealed record HelpRun(string Text, bool Bold=false, bool Code=false, string? Target=null);
public sealed record HelpBlock(IReadOnlyList<HelpRun> Runs, int Heading=0, bool Code=false, string? Anchor=null);

/// <summary>Small offline help formatter. Links are data, never commands or file paths.</summary>
public static class HelpDocument
{
    public static string Anchor(string text) => Regex.Replace(text.Replace("**", "").Trim().TrimStart('#').Trim().ToLowerInvariant(), @"\s+", "-");
    public static IReadOnlyList<HelpBlock> Parse(string source)
    {
        var blocks=new List<HelpBlock>();bool fenced=false;
        foreach(string original in source.Replace("\r", "").Split('\n'))
        {
            string line=original.Trim();
            if(line.StartsWith("```",StringComparison.Ordinal)){fenced=!fenced;continue;}
            if(fenced){blocks.Add(new(new[]{new HelpRun(original,false,true)},Code:true));continue;}
            if(line.Length==0)continue;
            var heading=Regex.Match(line,@"^(#{1,6})\s+(.+)$");
            int level=heading.Success?heading.Groups[1].Length:0;
            string text=heading.Success?heading.Groups[2].Value:line;
            text=text.Replace(@"$\times$","×");
            blocks.Add(new(Inline(text,level>0),level,Anchor:level>0?Anchor(text):null));
        }
        return blocks;
    }
    private static IReadOnlyList<HelpRun> Inline(string text,bool heading)
    {
        var runs=new List<HelpRun>();bool bold=heading,code=false;
        int start=0;
        void Add(int end){if(end>start)runs.Add(new(code?text[start..end]:text[start..end].Replace("|"," — "),bold,code));}
        for(int i=0;i<text.Length;)
        {
            if(text[i]=='`'){Add(i);code=!code;start=++i;continue;}
            if(!code&&i+1<text.Length&&text[i..(i+2)]=="**")
            {Add(i);bold=heading||!bold;i+=2;start=i;continue;}
            if(!code&&text[i]=='[')
            {
                int labelEnd=text.IndexOf("](",i,StringComparison.Ordinal);
                int end=labelEnd>=0?text.IndexOf(')',labelEnd+2):-1;
                if(end>=0)
                {
                    Add(i);
                    string target=text[(labelEnd+2)..end].Trim();
                    string? safe=target.StartsWith('#')?"#"+Anchor(target):PublicWebUrl.TryNormalize(target,out var url)?url:null;
                    runs.AddRange(Inline(text[(i+1)..labelEnd],bold).Select(r=>r with{Target=safe}));
                    i=end+1;start=i;continue;
                }
            }
            i++;
        }
        Add(text.Length);return runs;
    }
}
