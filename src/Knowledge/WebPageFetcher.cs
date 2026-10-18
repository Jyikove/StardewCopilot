using System.Net;
using System.Text.Json;

namespace StardewCodexChat.Knowledge;

internal interface IWebSearchProvider
{
    Task<string[]> SearchAsync(string keywords,CancellationToken token);
}
internal sealed record FetchedPage(string Title,string Url,List<TextChunk> Chunks,int Status,string ETag,string LastModified);
internal sealed class WebPageFetcher : IWebSearchProvider
{
    private readonly HttpClient http;
    private DateTimeOffset lastRequest;
    internal WebPageFetcher(HttpClient? http=null)=>this.http=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(12)};
    private async Task<(int Status,string Body,string ETag,string Modified)> Get(string parameters,string etag,string modified,CancellationToken token)
    {
        for(int attempt=0;attempt<2;attempt++)
        {
            var delay=lastRequest.AddMilliseconds(450)-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
            lastRequest=DateTimeOffset.UtcNow;
            var uri=new Uri("https://stardewvalleywiki.com/mediawiki/api.php?format=json&formatversion=2&"+parameters);
            for(int redirects=0;redirects<4;redirects++)
            {
                using var req=new HttpRequestMessage(HttpMethod.Get,uri) { Version=HttpVersion.Version20, VersionPolicy=HttpVersionPolicy.RequestVersionOrLower };
                req.Headers.UserAgent.ParseAdd("StardewCopilot/0.10 (personal game knowledge cache)");
                if(etag.Length>0)req.Headers.TryAddWithoutValidation("If-None-Match",etag);
                if(modified.Length>0)req.Headers.TryAddWithoutValidation("If-Modified-Since",modified);
                using var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
                int status=(int)response.StatusCode;
                if(status is >=300 and <400 && status!=304)
                {
                    uri=new Uri(uri,response.Headers.Location??throw new IOException("Missing redirect"));
                    if(uri.Scheme!="https"||uri.Host!="stardewvalleywiki.com"||!uri.IsDefaultPort||uri.UserInfo.Length>0)throw new IOException("Unsafe redirect refused");
                    continue;
                }
                if(status==429||status>=500){if(attempt==0){await Task.Delay(800,token);break;}throw new HttpRequestException("Wiki limited or unavailable");}
                if(status==304)return(304,"",etag,modified);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>2*1024*1024)throw new IOException("Wiki response too large");
                using var buffer=new MemoryStream();await using var stream=await response.Content.ReadAsStreamAsync(token);
                byte[] bytes=new byte[8192];int n;
                while((n=await stream.ReadAsync(bytes.AsMemory(),token))>0){if(buffer.Length+n>2*1024*1024)throw new IOException("Wiki response too large");buffer.Write(bytes,0,n);}
                return(status,System.Text.Encoding.UTF8.GetString(buffer.ToArray()),response.Headers.ETag?.ToString()??"",response.Content.Headers.LastModified?.ToString("R")??"");
            }
        }
        throw new IOException("Wiki retry or redirect limit reached");
    }
    public async Task<string[]> SearchAsync(string keywords,CancellationToken token)
    {
        string? reference=KnowledgeIntent.ReferencePage(keywords);
        if(reference!=null)return new[]{reference};
        foreach(string variant in KnowledgeText.SearchVariants(keywords))
        {
            var response=await Get("action=query&list=search&srnamespace=0&srlimit=3&srsearch="+Uri.EscapeDataString(variant),"","",token);
            using var json=JsonDocument.Parse(response.Body);
            var urls=json.RootElement.GetProperty("query").GetProperty("search").EnumerateArray().Select(r=>KnowledgeText.Canonical("https://stardewvalleywiki.com/"+Uri.EscapeDataString(r.GetProperty("title").GetString()!.Replace(' ','_')))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(urls.Length>0)return urls;
        }
        return Array.Empty<string>();
    }

    internal async Task<FetchedPage> FetchAsync(string url,string etag,string modified,CancellationToken token)
    {
        url=KnowledgeText.Canonical(url);
        string title=Uri.UnescapeDataString(new Uri(url).AbsolutePath[1..]).Replace('_',' ');
        var response=await Get("action=parse&prop=text&redirects=1&page="+Uri.EscapeDataString(title),etag,modified,token);
        if(response.Status==304)return new(title,url,new(),304,etag,modified);
        using var json=JsonDocument.Parse(response.Body);var parse=json.RootElement.GetProperty("parse");
        title=parse.GetProperty("title").GetString()!;
        var chunks=KnowledgeText.Extract(parse.GetProperty("text").GetString()!);
        if(chunks.Sum(c=>c.Content.Length)>250000)throw new IOException("Article too large to cache reliably");
        return new(title,KnowledgeText.Canonical("https://stardewvalleywiki.com/"+Uri.EscapeDataString(title.Replace(' ','_'))),chunks,response.Status,response.ETag,response.Modified);
    }
}
