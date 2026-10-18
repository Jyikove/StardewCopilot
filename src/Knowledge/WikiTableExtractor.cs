using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;

namespace StardewCodexChat.Knowledge;

internal static class WikiTableExtractor
{
    internal static string Extract(HtmlNode table)
    {
        var lines=new List<string>();string[]? headers=null;
        var carry=new Dictionary<int,(string Text,int Remaining)>();
        foreach(var row in table.Descendants("tr").Where(r=>r.Ancestors("table").FirstOrDefault()==table))
        {
            var cells=row.ChildNodes.Where(n=>n.Name is "td" or "th").ToArray();if(cells.Length==0)continue;
            if(cells.All(c=>c.Name=="th"))
            {
                headers=cells.SelectMany(c=>Enumerable.Repeat(Cell(c),Math.Clamp(c.GetAttributeValue("colspan",1),1,30))).ToArray();continue;
            }
            var values=new SortedDictionary<int,string>();
            foreach(var pair in carry.ToArray())
            {
                if(pair.Value.Remaining<=0){carry.Remove(pair.Key);continue;}
                values[pair.Key]=pair.Value.Text;carry[pair.Key]=(pair.Value.Text,pair.Value.Remaining-1);
            }
            int column=0;
            foreach(var cell in cells)
            {
                while(values.ContainsKey(column))column++;
                string text=Cell(cell);int span=Math.Clamp(cell.GetAttributeValue("colspan",1),1,30);
                for(int i=0;i<span;i++)
                {
                    values[column]=text;int rows=Math.Clamp(cell.GetAttributeValue("rowspan",1),1,200);
                    if(rows>1)carry[column]=(text,rows-1);column++;
                }
            }
            if(headers==null){lines.Add(string.Join(" | ",values.Values));continue;}
            var fields=new List<string>();
            foreach(var value in values)
            {
                string label=value.Key<headers.Length?headers[value.Key]:"Field"+(value.Key+1);
                if(label.Equals("Image",StringComparison.OrdinalIgnoreCase)||value.Value.Length==0)continue;
                fields.Add(label+"="+value.Value);
            }
            if(fields.Count>0)lines.Add(string.Join(" | ",fields));
        }
        return string.Join("\n",lines);
    }
    private static string Cell(HtmlNode cell)
    {
        var clone=cell.CloneNode(true);
        foreach(var hidden in clone.Descendants().Where(n=>n.GetAttributeValue("style","").Contains("display: none",StringComparison.OrdinalIgnoreCase)||n.GetAttributeValue("style","").Contains("display:none",StringComparison.OrdinalIgnoreCase)).ToArray())hidden.Remove();
        foreach(var img in clone.Descendants("img").ToArray())
        {
            string alt=img.GetAttributeValue("alt","");
            if(alt.Contains("Quality",StringComparison.OrdinalIgnoreCase))img.ParentNode.ReplaceChild(HtmlNode.CreateNode("<span> "+WebUtility.HtmlEncode(alt.Replace(".png",""))+" </span>"),img);
        }
        string text=WebUtility.HtmlDecode(clone.InnerText);
        text=Regex.Replace(text,@"\s*\(https://[^\s)]+\)","");
        return Regex.Replace(text,@"\s+"," ").Trim();
    }
}
