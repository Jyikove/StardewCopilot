using System.Net;
using System.Text.Json;
using StardewCodexChat.Knowledge;

int passed=0;void Check(bool ok,string title){if(!ok)throw new Exception(title);passed++;Console.WriteLine("PASS "+title);}
string root=Path.Combine(Path.GetTempPath(),"copilot-knowledge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string path=Path.Combine(root,"knowledge.sqlite");var logs=new List<string>();
string html="<div class='mw-parser-output'><script>evil()</script><h2>Gifts</h2><p>Haley loves Sunflower, Coconut and Pink Cake. These gifts are listed on this test page, which is fixture data and not actual game evidence.</p><table><tr><th>Item</th><th>Preference</th></tr><tr><td>Sunflower</td><td>Love</td></tr></table><h2>Schedule</h2><p>Schedule varies by season and weather, so consult the relevant section before making plans with the player.</p></div>";
var handler=new FakeHandler();handler.Html=html;
using var client=new HttpClient(handler);var fetcher=new WebPageFetcher(client);
var options=new KnowledgeOptions();var manager=new KnowledgeCacheManager(path,options,m=>{logs.Add(m);Console.WriteLine(m);},fetcher);
try
{
    Check(KnowledgeText.Canonical("https://stardewvalleywiki.com/Haley?utm_x=1#Gifts")=="https://stardewvalleywiki.com/Haley","canonical strips tracking and anchors");
    foreach(string url in new[]{"file:///etc/passwd","http://localhost/a","https://127.0.0.1/a","https://stardewvalleywiki.com.evil.test/Haley","https://user@stardewvalleywiki.com/Haley","https://stardewvalleywiki.com/Special:Search"})
    {try{KnowledgeText.Canonical(url);throw new Exception("unsafe URL accepted");}catch(InvalidOperationException){passed++;}}
    Check(KnowledgeText.SearchVariants("Haley gifts love").Contains("haley"),"gift intent falls back to NPC name");
    Check(KnowledgeText.SearchVariants("Dinosaur Egg obtain").Contains("dinosaur egg"),"acquisition intent falls back to item name");
    Check(KnowledgeText.SearchVariants("恐龙蛋 Dinosaur Egg").Contains("dinosaur egg"),"mixed Chinese English search is normalized");
    var chunks=KnowledgeText.Extract(html);
    Check(chunks.Any(c=>c.Heading=="Gifts")&&!string.Join("",chunks).Contains("evil()"),"DOM extraction preserves headings and removes scripts");
    Check(chunks.Any(c=>c.Content.Contains("Item=Sunflower | Preference=Love")),"table column relationships survive extraction");
    Check(TextChunker.Split(new[]{new TextChunk("Long",new string('a',12000))}).All(c=>c.Content.Length<=3000),"oversized sections are bounded");
    try{KnowledgeText.Extract("<html>access denied</html>");throw new Exception();}catch(InvalidOperationException){passed++;}
    string first=await manager.RetrieveAsync("Haley gifts love",default);
    Check(first.Contains("Sunflower")&&first.Contains("https://stardewvalleywiki.com/Haley"),"empty database retrieves actual page body and source");
    using(var result=JsonDocument.Parse(first))Check(!result.RootElement.GetProperty("cacheOnly").GetBoolean(),"online retrieval reports non-cache origin");
    string cachedOrigin=await manager.RetrieveAsync("Haley gifts love",default);
    using(var result=JsonDocument.Parse(cachedOrigin))Check(result.RootElement.GetProperty("cacheOnly").GetBoolean(),"local-only retrieval reports cache origin");
    int calls=handler.Calls;await manager.RetrieveAsync("Haley gifts love",default);
    Check(handler.Calls==calls,"repeat sufficient query performs zero HTTP calls");
    await manager.RetrieveAsync("海莉最喜欢什么礼物",default);
    Check(handler.Calls==calls,"Chinese alias expansion hits English evidence offline");
    var reopened=new KnowledgeCacheManager(path,options,m=>{logs.Add(m);Console.WriteLine(m);},fetcher);
    await reopened.RetrieveAsync("Haley gifts love",default);Check(handler.Calls==calls,"database survives reopening with no refetch or index rebuild");
    using(var db=new KnowledgeDatabase(path))
    {
        Check(db.Query("SELECT count(*) n FROM Pages")[0]["n"]=="1","canonical page URLs are unique");
        Check(db.Query("PRAGMA foreign_keys")[0].Values.First()=="1","foreign keys enabled");
        Check(LocalKnowledgeSearch.Search(db,KnowledgeText.Tokens("Strawberry seeds")).Count==0,"unrelated pages do not count as evidence");
        db.Execute("UPDATE Pages SET ExpiresAt=0");
    }
    handler.NotModified=true;
    await manager.RetrieveAsync("Haley gifts love",default);
    Check(handler.ConditionalRequests>0,"expired known pages use conditional refresh before search");
    Check(handler.Searches==1,"304 refresh reuses known page without repeating search");
    using(var db=new KnowledgeDatabase(path)){Check(db.Query("SELECT count(*) n FROM Chunks")[0]["n"]==chunks.Count.ToString(),"unchanged page does not duplicate chunks");db.Execute("UPDATE Pages SET ExpiresAt=0");}
    handler.NotModified=false;handler.Html=html.Replace("Sunflower","FixtureReplacement");
    await manager.RetrieveAsync("Haley gifts love",default);
    using(var db=new KnowledgeDatabase(path))
    {
        Check(!string.Join("",db.Query("SELECT Content FROM Chunks").SelectMany(r=>r.Values)).Contains("Sunflower"),"changed body replaces old chunks and index");
        Check(db.Query("SELECT Id FROM Pages")[0]["Id"]=="1","refresh retains stable page ID");db.Execute("UPDATE Pages SET ExpiresAt=0");
    }
    using(var db=new KnowledgeDatabase(path)){db.Execute("PRAGMA user_version=1");}
    using(var db=new KnowledgeDatabase(path)){Check(db.Query("PRAGMA user_version")[0].Values.First()=="2"&&db.Query("SELECT Id FROM Pages")[0]["Id"]=="1","schema migration preserves pages and adds indexes incrementally");}
    handler.Fail=true;
    string stale=await manager.RetrieveAsync("Haley gifts love",default);
    Check(stale.Contains("\"stale\":true")&&stale.Contains("FixtureReplacement"),"offline fallback marks old evidence stale");
    string none=await manager.RetrieveAsync("Iridium sprinkler recipes",default);
    Check(none.Contains("\"quality\":\"Low\"")&&none.Contains("\"chunks\":[]"),"network failure without relevant cache yields no invented evidence");
    handler.Fail=false;
    await manager.RetrieveAsync("Haley festivals winter",default);
    Check(handler.Searches>1,"incomplete evidence triggers supplementary search");
    options.MaxMegabytes=5;
    using(var db=new KnowledgeDatabase(path))
    {
        for(int i=0;i<8;i++)db.Execute("INSERT INTO Pages(CanonicalUrl,Title,ContentText,ExpiresAt,AccessCount,LastAccessedAt) VALUES(?,?,?,?,?,?)","https://stardewvalleywiki.com/Fixture_"+i,"obsolete",new string('x',1024*1024),0,0,0);
    }
    await manager.RetrieveAsync("Haley gifts love latest",default);
    using(var db=new KnowledgeDatabase(path))
    {
        Check(db.Query("SELECT count(*) n FROM Pages WHERE Title='obsolete'")[0]["n"]=="0"&&db.Query("SELECT Id FROM Pages WHERE Title='Haley'")[0]["Id"]=="1","capacity maintenance evicts expired unused pages while preserving useful source IDs");
        Check(long.Parse(db.Query("SELECT ExpiresAt FROM Pages WHERE Title='Haley'")[0]["ExpiresAt"])<=DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),"latest-information refresh uses the shorter configurable TTL");
    }
    string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"keep");
    var broken=new KnowledgeCacheManager(Path.Combine(blocked,"db"),options,m=>{logs.Add(m);Console.WriteLine(m);},fetcher);
    string fallback=await broken.RetrieveAsync("Haley gifts love",default);
    Check(fallback.Contains("FixtureReplacement"),"database failure still permits network evidence and a response");
    using(var db=new KnowledgeDatabase(path))
    {
        db.Execute("UPDATE Pages SET ExpiresAt=0");db.Execute("PRAGMA user_version=99");
    }
    var incompatible=new KnowledgeCacheManager(path,options,m=>{logs.Add(m);Console.WriteLine(m);},fetcher);await incompatible.RetrieveAsync("Haley gifts love",default);
    using(var reset=new RawVersion(path)){ } // validates the existing file was not deleted during a version error
    Check(File.Exists(path)&&logs.Any(l=>l.StartsWith("DatabaseError")),"newer schema fails safely without deleting persisted cache");
    Check(logs.Any(l=>l.StartsWith("LocalCacheHit"))&&logs.Any(l=>l.StartsWith("CacheExpired"))&&logs.Any(l=>l.StartsWith("KnowledgeContextBuilt")),"diagnostics distinguish cache and network states");
    using(var strictHttp=new HttpClient(new FakeHandler{Html=html,StrictSearch=true}))
    {
        var strictFetcher=new WebPageFetcher(strictHttp);string strictPath=Path.Combine(root,"strict.sqlite");
        using(var strictDb=new KnowledgeDatabase(strictPath))strictDb.Execute("INSERT INTO SearchCache(Query,Provider,Keywords,Urls,SearchedAt,ExpiresAt,Status) VALUES(?,?,?,?,?,?,?)","haley gifts love","MediaWiki","haley gifts love","[]",0,DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),"success");
        var strict=new KnowledgeCacheManager(strictPath,fetcher:strictFetcher);
        string fixedResult=await strict.RetrieveAsync("Haley gifts love",default);
        Check(fixedResult.Contains("Sunflower"),"legacy cached empty result cannot block new broader keyword search");
    }
    var emptyHandler=new FakeHandler{AlwaysEmpty=true};using(var emptyHttp=new HttpClient(emptyHandler))
    {
        var emptyFetcher=new WebPageFetcher(emptyHttp);var empty=new KnowledgeCacheManager(Path.Combine(root,"empty.sqlite"),fetcher:emptyFetcher);
        await empty.RetrieveAsync("unfindable item obtain",default);int emptyCalls=emptyHandler.Calls;
        await empty.RetrieveAsync("unfindable item obtain",default);
        Check(emptyCalls==emptyHandler.Calls,"new negative search cache prevents repeated HTTP for five minutes");
    }
    Check(!StardewCodexChat.Core.PublicWebUrl.TryNormalize("https://127.0.0.1/a",out _)&&!StardewCodexChat.Core.PublicWebUrl.TryNormalize("file:///etc/passwd",out _),"broad search rejects local targets and non-HTTPS URLs");
    var broadHandler=new BroadHandler();using(var broadHttp=new HttpClient(broadHandler))
    {
        var broad=new BroadWebSearch(Path.Combine(root,"broad.sqlite"),_=>Task.FromResult("dummy-web-key"),http:broadHttp);
        string evidence=await broad.RetrieveAsync("smapi installation",default);
        Check(evidence.Contains("https://smapi.io/installation")&&evidence.Contains("fixture installation"),"broad web search reads actual fetched content with its URL");
        Check(broadHandler.Searches==1&&broadHandler.Fetches==1,"empty search content triggers Fetch instead of using summary as body");
        int before=broadHandler.Searches+broadHandler.Fetches;await broad.RetrieveAsync("smapi installation",default);
        Check(before==broadHandler.Searches+broadHandler.Fetches,"broad web body cache prevents repeat Search/Fetch calls");
        broadHandler.Fail=true;string unavailable=await broad.RetrieveAsync("unrelated missing mechanic",default);
        Check(unavailable.Contains("\"chunks\":[]"),"broader provider failure without relevant evidence does not invent sources");
    }
    using(var routerHttp=new HttpClient(new BroadHandler()))
    using(var wikiEmptyHttp=new HttpClient(new FakeHandler{AlwaysEmpty=true}))
    {
        var emptyWiki=new KnowledgeCacheManager(Path.Combine(root,"router.sqlite"),fetcher:new WebPageFetcher(wikiEmptyHttp));
        var web=new BroadWebSearch(Path.Combine(root,"router.sqlite"),_=>Task.FromResult("dummy-web-key"),http:routerHttp);
        var router=new KnowledgeRouter(emptyWiki,web);string evidence=await router.RetrieveAsync("smapi installation",default);
        Check(evidence.Contains("https://smapi.io/installation")&&evidence.Contains("expanded to broader"),"Wiki miss automatically routes to broader search and returns real page evidence");
    }
    if(args.Contains("--inspect-smoker"))
    {
        string userDb=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","StardewCopilot","knowledge.sqlite");
        var inspector=new BroadWebSearch(userDb,_=>throw new Exception("must never access authentication or network"));
        string? smoke=await inspector.TryCacheAsync("熏鱼机要怎么获得",default);
        Check(smoke!=null,"actual saved smoker evidence is reusable for Chinese questions without network or Key access");
        using var result=JsonDocument.Parse(smoke!);Check(result.RootElement.GetProperty("cacheOnly").GetBoolean(),"actual local smoker evidence reports cacheOnly=true");
    }
    var decoys=new List<Dictionary<string,string>>{
        new(){{"Title","Best Summer Crops"},{"HeadingPath","Summer"},{"Content","Summer crop planting advice."}},
        new(){{"Title","Fish Smoker"},{"HeadingPath","Fish"},{"Content","A machine makes smoked fish; requires jelly."}}
    };
    Check(LocalKnowledgeSearch.Quality(decoys,KnowledgeText.QueryTerms("夏季有什么鱼可以钓"),.75)=="Low","summer crops plus Fish Smoker never count as a seasonal fishing list");
    Check(KnowledgeIntent.ReferencePage("夏季有什么鱼可以钓")=="https://stardewvalleywiki.com/Summer","seasonal fish questions resolve to the Wiki season reference page");
    string fishTable="<div class='mw-parser-output'><h2>Fish</h2><table><tr><th>Name</th><th>Location</th><th>Time</th><th>Weather</th><th>Season</th></tr><tr><td>Fixture Fish One</td><td rowspan='2'>Ocean</td><td>12:00–16:00</td><td>Sun</td><td>Summer</td></tr><tr><td>Fixture Fish Two</td><td>06:00–19:00</td><td>Any</td><td>Summer</td></tr></table></div>";
    var extracted=KnowledgeText.Extract(fishTable);
    Check(extracted.Any(c=>c.Content.Contains("Name=Fixture Fish Two | Location=Ocean | Time=06:00–19:00")),"table rowspan expansion keeps each fish's location and time aligned");
    handler.Fail=false;var refreshManager=new KnowledgeCacheManager(Path.Combine(root,"refresh.sqlite"),fetcher:fetcher);
    await refreshManager.RetrieveAsync("Haley gifts love",default);int priorSearches=handler.Searches;
    await refreshManager.RetrieveAsync("Haley gifts love",default,true);
    Check(handler.Searches>priorSearches,"refresh=true bypasses sufficient cache and cached search results");
    var uncachedHandler=new FakeHandler{Html=html};using(var uncachedHttp=new HttpClient(uncachedHandler))
    {
        string isolated=Path.Combine(root,"research-new","knowledge.sqlite");
        var uncached=new KnowledgeCacheManager(isolated,fetcher:new WebPageFetcher(uncachedHttp));
        string online=await uncached.RetrieveUncachedAsync("Haley gifts love",default);
        Check(online.Contains("Sunflower")&&!Directory.Exists(Path.GetDirectoryName(isolated)),"uncached Wiki retrieval fetches real body without creating a database or directory");
        int count=uncachedHandler.Calls;
        await uncached.RetrieveUncachedAsync("Haley gifts love",default);
        Check(uncachedHandler.Calls>count,"uncached Wiki retrieval repeats live search and fetch on every call");
        await uncached.RetrieveAsync("Haley gifts love",default);
        byte[] before=File.ReadAllBytes(isolated);uncachedHandler.Html=html.Replace("Sunflower","FreshOnlyFixture");
        string replaced=await uncached.RetrieveUncachedAsync("Haley gifts love",default);
        Check(replaced.Contains("FreshOnlyFixture")&&!replaced.Contains("Sunflower")&&File.ReadAllBytes(isolated).SequenceEqual(before),
            "uncached Wiki ignores preexisting evidence, uses fresh body and leaves cache bytes unchanged");
        uncachedHandler.Fail=true;string failed=await uncached.RetrieveUncachedAsync("Haley gifts love",default);
        Check(failed.Contains("\"chunks\":[]")&&!failed.Contains("Sunflower")&&File.ReadAllBytes(isolated).SequenceEqual(before),
            "uncached Wiki never falls back to old database evidence on network failure");
    }
    var uncachedBroadHandler=new BroadHandler();using(var uncachedBroadHttp=new HttpClient(uncachedBroadHandler))
    {
        string isolated=Path.Combine(root,"research-web-new","knowledge.sqlite");
        var uncachedBroad=new BroadWebSearch(isolated,_=>Task.FromResult("dummy-web-key"),http:uncachedBroadHttp);
        string online=await uncachedBroad.RetrieveAsync("smapi installation",default,true,true);
        Check(online.Contains("fixture installation")&&!Directory.Exists(Path.GetDirectoryName(isolated)),"uncached broader search and Fetch do not create a knowledge database");
        await uncachedBroad.RetrieveAsync("smapi installation",default);
        byte[] before=File.ReadAllBytes(isolated);int searches=uncachedBroadHandler.Searches;
        await uncachedBroad.RetrieveAsync("smapi installation",default,true,true);
        Check(uncachedBroadHandler.Searches>searches&&File.ReadAllBytes(isolated).SequenceEqual(before),"uncached broader search bypasses existing cache without changing it");
        uncachedBroadHandler.Fail=true;string failed=await uncachedBroad.RetrieveAsync("smapi installation",default,true,true);
        Check(failed.Contains("\"chunks\":[]")&&File.ReadAllBytes(isolated).SequenceEqual(before),"uncached broader search cannot silently substitute cache after network failure");
    }
    using(var uncachedRouterWikiHttp=new HttpClient(new FakeHandler{AlwaysEmpty=true}))
    using(var uncachedRouterWebHttp=new HttpClient(new BroadHandler()))
    {
        string isolated=Path.Combine(root,"research-router","knowledge.sqlite");
        var uncachedRouter=new KnowledgeRouter(new KnowledgeCacheManager(isolated,fetcher:new WebPageFetcher(uncachedRouterWikiHttp)),
            new BroadWebSearch(isolated,_=>Task.FromResult("dummy-web-key"),http:uncachedRouterWebHttp),false);
        string online=await uncachedRouter.RetrieveUncachedAsync("smapi installation",default);
        Check(online.Contains("https://smapi.io/installation")&&online.Contains("discovery budget")&&!Directory.Exists(Path.GetDirectoryName(isolated)),
            "explicit uncached research may use broader web while ordinary fallback is disabled, without touching cache");
    }
    if(args.Contains("--live"))
    {
        string livePath=Path.Combine(root,"live.sqlite");var live=new KnowledgeCacheManager(livePath,log:Console.WriteLine);
        string evidence=await live.RetrieveAsync("Strawberry",default);
        Check(evidence.Contains("stardewvalleywiki.com/Strawberry")&&evidence.Contains("chunks"),"live Wiki API search, DOM parse and native SQLite work under game .NET 6");
        using var doc=JsonDocument.Parse(evidence);Check(doc.RootElement.GetProperty("chunks").GetArrayLength()>0,"live retrieval contains page body rather than search snippets");
        foreach(var q in new[]{"Dinosaur Egg obtain","恐龙蛋 Dinosaur Egg","Haley gifts love","Haley 最爱 礼物"})
        {
            string result=await live.RetrieveAsync(q,default);using var parsed=JsonDocument.Parse(result);
            Check(parsed.RootElement.GetProperty("chunks").GetArrayLength()>0,"real failed player query now has Wiki evidence: "+q);
        }
        string seasonal=await live.RetrieveAsync("夏季有什么鱼可以钓",default);
        using(var result=JsonDocument.Parse(seasonal))
        {
            var fishEvidence=result.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
            Check(result.RootElement.GetProperty("quality").GetString()=="High"&&fishEvidence.Any(c=>c.GetProperty("url").GetString()=="https://stardewvalleywiki.com/Summer"),"live seasonal question locates the Summer Wiki page, not unrelated cache");
            Check(fishEvidence.Any(c=>c.GetProperty("body").GetString()!.Contains("Pufferfish")&&c.GetProperty("body").GetString()!.Contains("Time=")&&c.GetProperty("body").GetString()!.Contains("Weather=")),"actual model context includes summer fish names, times and weather as labeled records");
        }
        Console.WriteLine("LIVE sources checked; no DeepSeek key or player records used.");
    }
    if(args.Contains("--repair-fish-cache"))
    {
        string userPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","StardewCopilot","knowledge.sqlite");
        var repair=new KnowledgeCacheManager(userPath,log:Console.WriteLine);
        using var result=JsonDocument.Parse(await repair.RetrieveAsync("夏季有什么鱼可以钓",default,true));
        Check(result.RootElement.GetProperty("quality").GetString()=="High"&&result.RootElement.GetProperty("chunks").EnumerateArray().Any(c=>c.GetProperty("body").GetString()!.Contains("Pufferfish")),"actual shared knowledge database now contains verified summer fishing records");
    }
    if(args.Contains("--live-broad"))
    {
        var credential=new StardewCodexChat.Core.FileSecretStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","Steam","steamapps","common","Stardew Valley","Contents","MacOS","Mods","StardewCopilot","secrets.json"));
        var liveBroad=new BroadWebSearch(Path.Combine(root,"live-broad.sqlite"),credential.GetKeyAsync,Console.WriteLine);
        using var result=JsonDocument.Parse(await liveBroad.RetrieveAsync("dinosaur egg acquisition",default));
        Check(result.RootElement.GetProperty("chunks").GetArrayLength()>0,"official DeepSeek Search/Fetch returns usable live page evidence with configured authentication");
    }
    if(args.Contains("--init-user"))
    {
        string userPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","StardewCopilot","knowledge.sqlite");
        using var userDb=new KnowledgeDatabase(userPath);
        Check(userDb.Query("PRAGMA user_version")[0].Values.First()=="2","actual macOS user-data path is writable and supports native FTS5 under game runtime");
    }
    Console.WriteLine($"All {passed} knowledge checks passed");
}
finally{Directory.Delete(root,true);}
sealed class RawVersion(string path):IDisposable{public void Dispose(){if(!File.Exists(path))throw new Exception("database deleted");}}
sealed class FakeHandler:HttpMessageHandler
{
    public string Html="";public bool Fail,NotModified,StrictSearch,AlwaysEmpty;public int Calls,Searches,ConditionalRequests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Calls++;if(Fail)throw new HttpRequestException("fixture offline");
        string query=request.RequestUri!.Query;
        if(query.Contains("list=search")){Searches++;
            string keywords=Uri.UnescapeDataString(query.Split("srsearch=")[1]);
            if(AlwaysEmpty || (StrictSearch && keywords!="haley"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"query\":{\"search\":[]}}")});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"query\":{\"search\":[{\"title\":\"Haley\"}]}}")});}
        if(request.Headers.Contains("If-None-Match")){ConditionalRequests++;if(NotModified)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}
        var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{parse=new{title="Haley",text=Html}}))};response.Headers.ETag=new("\"fixture-v1\"");return Task.FromResult(response);
    }
}

sealed class BroadHandler:HttpMessageHandler
{
    public int Searches,Fetches;public bool Fail;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token)
    {
        if(req.Headers.Authorization?.Parameter!="dummy-web-key"||req.RequestUri!.Host!="api.deepseek.com")throw new Exception("invalid authorization transport");
        if(Fail)return new(HttpStatusCode.Forbidden){Content=new StringContent("sensitive fixture provider message")};
        using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
        string data;
        if(req.RequestUri.AbsolutePath.EndsWith("/search"))
        {
            Searches++;if(!body.RootElement.GetProperty("need_content").GetBoolean())throw new Exception("missing body request");
            data="{\"results\":[{\"title\":\"SMAPI installation\",\"url\":\"https://smapi.io/installation\",\"content\":\"\",\"summary\":\"not page body\",\"site_name\":\"SMAPI\",\"authority\":4,\"rerank_score\":2.5},{\"title\":\"unsafe\",\"url\":\"https://localhost/a\",\"content\":\"skip\"}]}";
        }
        else
        {
            Fetches++;data=JsonSerializer.Serialize(new{title="SMAPI installation",content="# Installation\n\n"+string.Concat(Enumerable.Repeat("SMAPI fixture installation instructions are test data, not real guidance. ",5))});
        }
        return new(HttpStatusCode.OK){Content=new StringContent(data)};
    }
}
