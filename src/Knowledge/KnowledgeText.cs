using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;

namespace StardewCodexChat.Knowledge;

internal sealed record TextChunk(string Heading, string Content);
internal static class KnowledgeText
{
    // Query expansion only; this dictionary contains no gameplay facts.
    private static readonly Dictionary<string,string> Aliases=new() {
        ["夏天"]="Summer",["春天"]="Spring",["秋天"]="Fall",["冬天"]="Winter",["能钓"]="Fish",["可钓"]="Fish",["钓到"]="Fish",["什么鱼"]="Fish",["哪些鱼"]="Fish",["鱼类"]="Fish",["可以钓"]="Fish",["钓什么"]="Fish",["熏鱼机"]="Fish Smoker",["洞穴凝胶"]="Cave Jelly",["海凝胶"]="Sea Jelly",["河凝胶"]="River Jelly",["恐龙蛋"]="Dinosaur Egg",["草莓"]="Strawberry",["海莉"]="Haley",["阿比盖尔"]="Abigail",["阿比"]="Abigail",["罗宾"]="Robin",["斧子"]="Axe",["煤炭"]="Coal",["煤矿"]="Coal",["礼物"]="Gifts",["送礼"]="Gifts",["喜欢"]="Gifts",["最爱"]="Love",["最喜欢"]="Love",["任务"]="Quests",["钓鱼"]="Fishing",["作物"]="Crops",["温室"]="Greenhouse",["洒水器"]="Sprinkler",["浇水"]="Watering",["献祭"]="Bundles",["社区中心"]="Community Center",["夏季"]="Summer",["春季"]="Spring",["冬季"]="Winter",["秋季"]="Fall",["赚钱"]="Gold",["远古水果"]="Ancient Fruit",["杨桃"]="Starfruit",["上古水果"]="Ancient Fruit"
    };
    internal static string Normalize(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"\s+"," ").Trim();
    internal static string Expand(string text)
    {
        string result=Normalize(text);
        foreach(var pair in Aliases) if(text.Contains(pair.Key))result+=" "+pair.Value.ToLowerInvariant();
        return result;
    }
    internal static string Tokens(string text)
    {
        text=Expand(text);
        var latin=Regex.Matches(text,@"[a-z0-9]{2,}").Select(m=>m.Value);
        var chinese=Regex.Matches(text,@"[\u4e00-\u9fff]+").SelectMany(m=>Enumerable.Range(0,Math.Max(0,m.Length-1)).Select(i=>m.Value.Substring(i,2)));
        return string.Join(" ",latin.Concat(chinese).Distinct());
    }
    internal static string QueryTerms(string query)
    {
        var stop=new HashSet<string>(new[]{"how","to","get","where","can","i","the","a","an","in","of","is","are","do","does","what","stardew","valley","wiki","obtain","obtained","acquisition","craft","crafting","latest"});
        return string.Join(" ",Tokens(query).Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>!stop.Contains(t)));
    }
    internal static string[] SearchVariants(string query)
    {
        string expanded=Expand(query);
        var words=Regex.Matches(expanded,@"[a-z0-9]+(?:'[a-z]+)?").Select(m=>m.Value).Distinct().Take(12).ToArray();
        var intent=new HashSet<string>(new[]{"obtain","obtained","craft","crafting","get","getting","find","where","how","to","the","a","an","in","stardew","valley","wiki","gifts","gift","love","loves","loved","favorite","favourite","likes","like","location","locations","acquisition","source","sources","best","latest"});
        string core=string.Join(" ",words.Where(w=>!intent.Contains(w)));
        string english=string.Join(" ",words);
        return new[]{Normalize(query),english,core}.Where(v=>v.Length>0).Distinct().Take(3).ToArray();
    }
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Canonical(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="stardewvalleywiki.com"||!uri.IsDefaultPort||uri.UserInfo.Length>0)
            throw new InvalidOperationException("Only HTTPS official Wiki articles are allowed");
        string path=Uri.UnescapeDataString(uri.AbsolutePath).Replace(' ','_');
        if(path=="/"||path.Contains(':')||path.StartsWith("/mediawiki",StringComparison.OrdinalIgnoreCase)||path.Contains(".."))throw new InvalidOperationException("Not an article URL");
        return "https://stardewvalleywiki.com/"+string.Join("/",path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
    }
    internal static List<TextChunk> Extract(string html)
    {
        var doc=new HtmlDocument();doc.LoadHtml(html);
        var root=doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ',normalize-space(@class),' '),' mw-parser-output ')]")
            ??doc.DocumentNode.SelectSingleNode("//main//article|//article|//main");
        if(root==null)throw new InvalidOperationException("No reliable article body");
        foreach(var n in (root.SelectNodes(".//script|.//style|.//nav|.//footer|.//*[contains(@class,'mw-editsection') or contains(@class,'navbox') or @id='toc']")??new HtmlNodeCollection(root)).ToList())n.Remove();
        foreach(var link in root.SelectNodes(".//a[@href]")??new HtmlNodeCollection(root))
        {
            string href=link.GetAttributeValue("href","");
            if(href.StartsWith('/'))href="https://stardewvalleywiki.com"+href;
            try { string url=Canonical(href); string label=Clean(link.InnerText); if(label.Length>1)link.InnerHtml=WebUtility.HtmlEncode(label+" ("+url+")"); } catch(InvalidOperationException) { }
        }
        var pieces=new List<TextChunk>();string heading="Introduction";var path=new SortedDictionary<int,string>();
        foreach(var node in root.ChildNodes)
        {
            if(Regex.IsMatch(node.Name,"^h[1-6]$"))
            {
                int level=node.Name[1]-'0';foreach(int key in path.Keys.Where(k=>k>=level).ToArray())path.Remove(key);
                path[level]=Clean(node.InnerText);heading=string.Join(" / ",path.Values);continue;
            }
            string text;
            if(node.Name=="table")
                text=WikiTableExtractor.Extract(node);
            else text=Clean(node.InnerText);
            if(text.Length>30)pieces.Add(new(heading,text));
        }
        if(pieces.Sum(c=>c.Content.Length)<80)throw new InvalidOperationException("Empty or incomplete article");
        return TextChunker.Split(pieces);
    }
    internal static List<TextChunk> MarkdownChunks(string title,string content)
    {
        var pieces=new List<TextChunk>();var path=new SortedDictionary<int,string>();string body="",heading=title;
        void Flush(){if(body.Trim().Length>0)pieces.Add(new(heading,body.Trim()));body="";}
        foreach(string line in content.Replace("\r","").Split('\n'))
        {
            var match=Regex.Match(line,@"^(#{1,6})\s+(.+)$");
            if(match.Success)
            {
                Flush();int level=match.Groups[1].Length;foreach(int key in path.Keys.Where(k=>k>=level).ToArray())path.Remove(key);
                path[level]=match.Groups[2].Value;heading=title+" / "+string.Join(" / ",path.Values);
            }
            else if(line.Trim().Length==0)Flush();
            else body+=line+"\n";
        }
        Flush();return TextChunker.Split(pieces);
    }
    private static string Clean(string text)=>Regex.Replace(WebUtility.HtmlDecode(text),@"\s+"," ").Trim();
}
internal static class TextChunker
{
    internal static List<TextChunk> Split(IEnumerable<TextChunk> pieces)
    {
        var result=new List<TextChunk>();string heading="",body="";
        foreach(var piece in pieces)
        {
            if((piece.Heading!=heading||body.Length+piece.Content.Length>2600)&&body.Length>0){result.Add(new(heading,body));body="";}
            heading=piece.Heading;
            // Tables stay intact up to a generous bound; very large tables split at row boundaries.
            foreach(string row in piece.Content.Split('\n'))
            {
                for(int start=0;start<row.Length;start+=2400)
                {
                    string part=row.Substring(start,Math.Min(2400,row.Length-start));
                    if(body.Length+part.Length>3000){result.Add(new(heading,body));body=body.Length>150?body[^150..]:"";}
                    body+=part+"\n";
                }
            }
        }
        if(body.Length>0)result.Add(new(heading,body));return result.DistinctBy(c=>KnowledgeText.Hash(c.Content)).ToList();
    }
}
