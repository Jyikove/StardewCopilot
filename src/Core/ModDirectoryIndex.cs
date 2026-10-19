using System.Text.Json;

namespace StardewCodexChat.Core;

/// <summary>Public SMAPI metadata has no directory API. Match bounded local manifests;
/// unavailable or ambiguous paths remain unreadable instead of using internal SMAPI members.</summary>
public static class ModDirectoryIndex
{
    public static Dictionary<string,string> Discover(string root)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Path,int Depth)>();
        pending.Enqueue((root,0));
        int visited=0;
        while(pending.Count>0 && visited++<1000)
        {
            var (path,depth)=pending.Dequeue();
            try
            {
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                string manifest=Path.Combine(path,"manifest.json");
                if(File.Exists(manifest))
                {
                    if((File.GetAttributes(manifest)&FileAttributes.ReparsePoint)!=0)continue;
                    using var stream=File.OpenRead(manifest);
                    byte[] bytes=new byte[65537];int length=0,read;
                    while(length<bytes.Length&&(read=stream.Read(bytes,length,bytes.Length-length))!=0)length+=read;
                    if(length>65536)continue;
                    using var json=JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes,0,length).TrimStart('\uFEFF'),new JsonDocumentOptions{MaxDepth=16,CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
                    if(json.RootElement.TryGetProperty("UniqueID",out var id)&&id.ValueKind==JsonValueKind.String)
                    {
                        string key=id.GetString()!;
                        if(ambiguous.Contains(key))continue;
                        if(!result.TryAdd(key,path)){result.Remove(key);ambiguous.Add(key);}
                    }
                    continue;
                }
                if(depth>=5)continue;
                foreach(string child in Directory.EnumerateDirectories(path).Take(1000-visited-pending.Count))pending.Enqueue((child,depth+1));
            }
            catch(IOException){} catch(UnauthorizedAccessException){} catch(JsonException){}
        }
        return result;
    }
}
