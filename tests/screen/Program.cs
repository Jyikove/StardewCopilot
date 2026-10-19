using System.Net;
using System.Text;
using System.Text.Json;
using StardewCodexChat;
using StardewCodexChat.Core;

int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
async Task<Exception> Failure(Func<Task> action){try{await action();}catch(Exception e){return e;}throw new Exception("Expected failure");}
var options=new ChatOptions();
ScreenFrame Frame(byte value=1)=>new(1200,800,Enumerable.Repeat(value,1200*800*4).ToArray(),DateTimeOffset.UtcNow);
string OneCrop="{\"regions\":[{\"x\":0.25,\"y\":0.2,\"width\":0.5,\"height\":0.3}]}";
string Recapture(string cause="capture_quality")=>JsonSerializer.Serialize(new{cause,reason="局部图文字因截屏暂时损坏仍不清楚",expected_visible_information="当前游戏已显示的数量文字"});
HttpResponseMessage Json(object obj)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(obj),Encoding.UTF8,"application/json")};
HttpResponseMessage Answer(string text="画面中数量不清楚，请先悬停物品查看名称。")=>Json(new{choices=new[]{new{finish_reason="stop",message=new{role="assistant",content=text}}}});
HttpResponseMessage Tool(string name,string args)=>Json(new{choices=new[]{new{finish_reason="tool_calls",message=new{role="assistant",content="",tool_calls=new[]{new{id="screen-tool",type="function",function=new{name,arguments=args}}}}}}});
string[] ImageUrls(JsonElement root)=>root.GetProperty("messages")[1].GetProperty("content").EnumerateArray().Where(p=>p.GetProperty("type").GetString()=="image_url").Select(p=>p.GetProperty("image_url").GetProperty("url").GetString()!).ToArray();
bool HasTool(JsonElement root,string name)=>root.TryGetProperty("tools",out var tools)&&tools.EnumerateArray().Any(t=>t.GetProperty("function").GetProperty("name").GetString()==name);

Check(ChatCommands.Parse("/ScReEn 帮我看看背包").Kind==ChatCommandKind.Screen&&ChatCommands.Parse("/screen “看看背包”").Argument=="看看背包","screen command parses case-insensitively and unquotes the original question");
Check(CommandCompletion.Entries.Any(e=>e.Name=="/screen"&&e.NeedsQuestion),"screen is available in slash completion with a question suffix");
var original=Frame();var region=ScreenVision.ParseRegions(OneCrop,original).Single();
Check(region==new ScreenRegion(240,136,720,288),"normalized coordinates use original 1200x800 pixels plus ten-percent context margins");
var edge=ScreenVision.ParseRegions("{\"regions\":[{\"x\":0,\"y\":0,\"width\":1,\"height\":1}]}",original).Single();
Check(edge==new ScreenRegion(0,0,1200,800),"padding clamps at image edges without losing the image");
foreach(var bad in new[]{"{\"regions\":[]}","{\"regions\":[{\"x\":-0.1,\"y\":0,\"width\":0.5,\"height\":0.5}]}","{\"regions\":[{\"x\":0.9,\"y\":0,\"width\":0.5,\"height\":0.5}]}","{\"regions\":[{\"x\":0,\"y\":0,\"width\":0,\"height\":0.5}]}","{\"regions\":[{\"x\":0,\"y\":0,\"width\":1e300,\"height\":0.5}]}"})
 Check(await Failure(()=>Task.FromResult(ScreenVision.ParseRegions(bad,original))) is ArgumentException,"invalid/outside/empty region rejected locally");
Check(await Failure(()=>Task.Run(()=>new ScreenFrame(8192,8192,Array.Empty<byte>(),DateTimeOffset.Now).Validate())) is InvalidOperationException,"oversized or malformed original pixels are rejected");

var capture=new Capture(original);var processor=new Images();var requests=new List<JsonElement>();
using(var http=new HttpClient(new Handler(async request=>{using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return Answer("画面是背包。");})))
{
 string answer=await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"看看背包",capture,processor,default);
 Check(answer=="画面是背包。"&&capture.Count==1&&processor.Previews==1&&processor.Crops.Count==0&&requests.Count==1,"clear preview can answer immediately without mandatory crop or second request");
 Check(requests[0].GetProperty("model").GetString()=="deepseek-flash"&&ImageUrls(requests[0]).Single().EndsWith(Convert.ToBase64String(new byte[]{1,1})),"preview is sent as documented inline PNG image_url with the supported model");
 Check(requests[0].GetProperty("messages").GetArrayLength()==2&&!HasTool(requests[0],"add_todo"),"screen requests do not include historical dialog or write tools");
 Check(requests[0].GetProperty("messages")[0].GetProperty("content").GetString()!.Split(AssistantPersona.Voice).Length==2,"screen serialized request uses shared persona once without altering vision tools");
}
capture=new(original);processor=new();requests.Clear();
using(var http=new HttpClient(new Handler(async request=>{using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return requests.Count==1?Tool("screen_crop","{\"regions\":[{\"x\":0.1,\"y\":0.1,\"width\":0.2,\"height\":0.2},{\"x\":0.6,\"y\":0.6,\"width\":0.2,\"height\":0.2}]}"):Answer();})))
{
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"哪些能卖",capture,processor,default);
 Check(requests.Count==2&&capture.Count==1&&processor.Crops.Count==2,"multiple crop regions are combined into one detail request");
 Check(processor.Crops.All(c=>ReferenceEquals(c.Frame,original))&&ImageUrls(requests[1]).Length==2,"each crop comes from the retained original frame");
 Check(ImageUrls(requests[1]).All(u=>!ImageUrls(requests[0]).Contains(u)),"detail request omits the preview and full original");
 Check(!HasTool(requests[0],"screen_recapture")&&HasTool(requests[1],"screen_recapture"),"recapture is available after local detail analysis, not before the preview has been inspected");
}

foreach(string scenario in new[]{"new-frame","same-frame","scene-missing"})
{
 capture=new(original,scenario=="same-frame"?original:Frame(2));processor=new();requests.Clear();
 using var http=new HttpClient(new Handler(async request=>{
  using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
  return requests.Count switch{1=>Tool("screen_crop",OneCrop),2=>Tool("screen_recapture",Recapture(scenario=="scene-missing"?"scene_missing":"capture_quality")),3 when scenario=="new-frame"=>Tool("screen_crop",OneCrop),_=>Answer("只能确认当前画面可见的物品。")};
 }));
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"分析物品",capture,processor,default);
 if(scenario=="scene-missing"){
  Check(capture.Count==1&&requests.Count==3,"missing game-scene information cannot trigger recapture");
  Check(!requests[2].TryGetProperty("tools",out _),"scene insufficiency forces an honest final answer instead of more tools");
 }else if(scenario=="same-frame"){
  Check(capture.Count==2&&requests.Count==3&&processor.Previews==1,"unchanged original pixels stop the recapture loop without re-uploading a duplicate preview");
  Check(!requests[2].TryGetProperty("tools",out _),"unchanged recapture forces final synthesis");
 }else{
  Check(capture.Count==2&&processor.Previews==2&&processor.Crops.Count==2&&requests.Count==4,"changed recapture gets a new preview and optional new original crop but no third capture");
  Check(!HasTool(requests[3],"screen_recapture"),"recapture permission is permanently removed after one retry");
  Check(ReferenceEquals(processor.Crops.Last().Frame.Rgba,capture.Frames.Last().Rgba),"post-recapture cropping uses new original pixels, never the old frame");
 }
}

capture=new(original);processor=new();requests.Clear();
using(var http=new HttpClient(new Handler(async request=>{using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return requests.Count<8?Tool("screen_crop",OneCrop):Answer("已达到处理预算，不能确认该细节。");})))
{
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"模糊画面",capture,processor,default);
 Check(requests.Count==8&&capture.Count==1&&processor.Crops.Count==1&&!requests[^1].TryGetProperty("tools",out _),"repeated/unauthorized tools converge within eight rounds with one crop batch per frame");
}
int searches=0;capture=new(original);processor=new();requests.Clear();
using(var http=new HttpClient(new Handler(async request=>{using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return requests.Count<=2?Tool("wiki_search","{\"query\":\"Fish Smoker\"}"):Answer("先保留用途不明的物品[https://stardewvalleywiki.com/Fish_Smoker]，不要猜价格[https://example.com/fake]。");})))
{
 string answer=await new DeepSeekClient(new Secret(),http,knowledge:(_,_)=>{searches++;return Task.FromResult("{\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Fish_Smoker\",\"text\":\"fixture\"}]}");}).AskScreenAsync(options,"出售建议",capture,processor,default);
 Check(searches==2&&!HasTool(requests[^1],"wiki_search")&&answer.Contains("[https://stardewvalleywiki.com/Fish_Smoker]")&&!answer.Contains("example.com"),"mechanics retrieval is bounded and only actual retrieved citations survive");
}
capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(_=>throw new Exception("No request expected")))){
 Check(await Failure(()=>new DeepSeekClient(new Secret(),http).AskScreenAsync(options with{Model="deepseek-v4-pro"},"看看",capture,processor,default)) is InvalidOperationException&&capture.Count==0,"unsupported configured model is rejected before taking or uploading a screenshot");
 var blocked=new WaitingCapture();using var cancel=new CancellationTokenSource();
 var pending=new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"看看",blocked,processor,cancel.Token);
 await blocked.Started.Task;cancel.Cancel();Check(await Failure(()=>pending) is OperationCanceledException,"cancel while awaiting the game draw does not send an API request");
}
var root=Path.Combine(Path.GetTempPath(),"copilot-screen-test-"+Guid.NewGuid().ToString("N"));
try{
 var store=new ConversationStore(root);var prior=new Conversation();prior.AddExchange("old private history","old screenshot observations");await store.SaveAsync("save-a",prior.Snapshot());
 requests.Clear();capture=new(original);processor=new();
 using var http=new HttpClient(new Handler(async request=>{
  string raw=await request.Content!.ReadAsStringAsync();
  using var body=JsonDocument.Parse(raw);
  string system=body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
  Check(system.Contains("old private history")&&system.Contains("old screenshot observations")&&system.Contains("历史数据"),"screen API receives save-bound text history explicitly marked as historical data");
  requests.Add(body.RootElement.Clone());
  return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content="只能分析本次截图。"},finish_reason=(string?)null}}})+"\n\ndata: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=""},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
 }));
 var session=new ChatSession(new DeepSeekClient(new Secret(),http),store:store,screenCapture:capture,screenImages:processor);session.SwitchSave("save-a");
 async Task Finish(){using var timeout=new CancellationTokenSource(5000);while(session.IsBusy){session.Tick();await Task.Delay(2,timeout.Token);}}
 await Finish();session.Draft.Replace("/screen");Check(!session.Send(options)&&capture.Count==0,"bare screen command shows local usage without capturing");
 session.Draft.Replace("/screen 看看背包");Check(session.Send(options),"screen dispatches from the existing chat input");await Finish();
 var loaded=await store.LoadAsync("save-a");Check(loaded.State.Transcript.Length==4&&loaded.State.Transcript[^2].Text=="/screen 看看背包"&&loaded.State.Transcript[^1].Text=="只能分析本次截图。","screen question and answer persist in the existing per-save conversation");
 Check(!Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p).Contains("data:image")),"no screenshot or base64 image is persisted in conversation files");
 var waiting=new WaitingCapture();var switched=new ChatSession(new DeepSeekClient(new Secret(),http),screenCapture:waiting,screenImages:processor);switched.SwitchSave("save-a");switched.Draft.Replace("/screen 看看");switched.Send(options);await waiting.Started.Task;switched.SwitchSave("save-b");await Task.Delay(20);switched.Tick();
 Check(!switched.IsBusy&&switched.Display.Count==0,"changing saves cancels pending visual work without leaking the result into the next save");
}finally{Directory.Delete(root,true);}
// Context, fresh state, batched detail queries and inline references are retained through cropping.
requests.Clear();capture=new(original);processor=new();var gameQueries=new StateQueries();
var remembered=ConversationState.Empty with { Summary="museum collection is my long-term goal", Recent=new[]{new ChatMessage("user","keep artifacts for the museum"),new ChatMessage("assistant","old inventory count was 999")} };
HttpResponseMessage ReadBatch(params string[] names)=>Json(new{choices=new[]{new{finish_reason="tool_calls",message=new{role="assistant",content="",tool_calls=names.Select((name,i)=>new{id="read-"+i,type="function",function=new{name,arguments="{}"}}).ToArray()}}}});
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count switch {1=>ReadBatch("get_inventory","get_quests"),2=>Tool("screen_crop",OneCrop),3=>Tool("wiki_search","{\"query\":\"Artifacts\"}"),_=>Answer("先保留要捐赠的文物。")};
}))) {
 string answer=await new DeepSeekClient(new Secret(),http,knowledge:(_,_)=>Task.FromResult("{\"cacheOnly\":true,\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Artifacts\",\"text\":\"fixture verified donation mechanism\"}]}"))
  .AskScreenAsync(options,"结合之前的目标，哪些可以卖？",capture,processor,default,context:remembered,queries:gameQueries);
 Check(gameQueries.Calls.SequenceEqual(new[]{"get_overview","get_todos","get_inventory","get_quests"}),"screen automatically reads current overview and todos, then accepts batched fresh inventory and quest reads");
 Check(HasTool(requests[0],"get_inventory")&&HasTool(requests[0],"find_item")&&HasTool(requests[0],"get_npc")&&!HasTool(requests[0],"add_todo"),"screen exposes the existing read-only state tools without granting todo writes");
 Check(requests.All(r=>r.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("museum collection is my long-term goal")),"compressed summary and recent conversational intent survive every visual stage");
 string last=requests[^1].GetProperty("messages")[0].GetProperty("content").GetString()!;
 Check(last.Contains("CURRENT_INVENTORY_FIXTURE")&&last.Contains("CURRENT_QUEST_FIXTURE")&&last.Contains("CURRENT_TODO_FIXTURE")&&last.Contains("queriedAt"),"crop and final stages retain time-stamped fresh state and player todos");
 Check(ImageUrls(requests[2]).Single()!=ImageUrls(requests[0]).Single(),"adding text context does not re-upload the overview image in crop requests");
 Check(answer.EndsWith("[https://stardewvalleywiki.com/Artifacts]")&&CitationMarkup.Parse(answer).Any(p=>p.Url=="https://stardewvalleywiki.com/Artifacts"),"verified references are appended inline if omitted by the model and use the ordinary citation renderer, including cached evidence");
}
requests.Clear();gameQueries=new();capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count<4?ReadBatch("get_inventory","get_quests","get_overview"):Answer("依据当前状态回答。");
}))) {
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"看看当前存档",capture,processor,default,queries:gameQueries);
 Check(gameQueries.Calls.Count==10&&!HasTool(requests[^1],"get_inventory"),"game detail queries stop after eight reads plus the two startup observations");
}
requests.Clear();gameQueries=new();capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return Answer("当前画面与状态。");
}))) {
 var client=new DeepSeekClient(new Secret(),http);
 await client.AskScreenAsync(options,"第一次",capture,processor,default,queries:gameQueries);
 await client.AskScreenAsync(options,"第二次",capture,processor,default,queries:gameQueries);
 Check(gameQueries.Calls.Count==4&&requests[0].GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("OVERVIEW_1")&&requests[1].GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("OVERVIEW_2"),"each screen question reacquires state rather than reusing the prior request snapshot");
}
requests.Clear();capture=new(original);processor=new();int cacheLookups=0,freshLookups=0;
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());return requests.Count<3?Tool("wiki_search","{\"query\":\"Artifact uses\",\"refresh\":true}"):Answer("已重新核实用途[https://stardewvalleywiki.com/Artifacts]。");
}))) {
 await new DeepSeekClient(new Secret(),http,knowledge:(_,_)=>{cacheLookups++;return Task.FromResult("{}");},freshKnowledge:(_,_)=>{freshLookups++;return Task.FromResult("{\"cacheOnly\":false,\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Artifacts\"}]}");})
  .AskScreenAsync(options,"核实用途",capture,processor,default);
 Check(cacheLookups==0&&freshLookups==2,"screen knowledge requests honor refresh and reuse the existing fresh-retrieval provider");
}
HttpResponseMessage Mixed(params (string Name,string Arguments)[] calls)=>Json(new{choices=new[]{new{finish_reason="tool_calls",message=new{role="assistant",content="",tool_calls=calls.Select((c,i)=>new{id="mixed-"+i,type="function",function=new{name=c.Name,arguments=c.Arguments}}).ToArray()}}}});
requests.Clear();gameQueries=new();capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count==1?Mixed(("get_inventory","{}"),("screen_crop",OneCrop),("wiki_search","{\"query\":\"Artifacts\"}")):Answer("结合背包与画面保留文物。");
}))) {
 string answer=await new DeepSeekClient(new Secret(),http,knowledge:(_,_)=>Task.FromResult("{\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Artifacts\"}]}"))
  .AskScreenAsync(options,"综合分析",capture,processor,default,queries:gameQueries);
 Check(requests.Count==2&&gameQueries.Calls.Contains("get_inventory")&&processor.Crops.Count==1&&answer.Contains("[https://stardewvalleywiki.com/Artifacts]"),"mixed inventory, crop and retrieval calls execute in one batch instead of being rejected and repeated");
 Check(requests[1].GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("CURRENT_INVENTORY_FIXTURE")&&ImageUrls(requests[1]).Single()!=ImageUrls(requests[0]).Single(),"mixed results reach the same detail request with current state and original crops");
}
requests.Clear();capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count==1?Mixed(("screen_crop",OneCrop),("screen_recapture",Recapture())):Answer("只需要这张原图的局部。");
}))) {
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"看看",capture,processor,default);
 Check(capture.Count==1&&processor.Crops.Count==1,"a mixed batch cannot recapture before the model has actually seen crop results");
}
requests.Clear();gameQueries=new();capture=new(original);processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count switch {1=>Tool("screen_crop",OneCrop),2=>Tool("screen_recapture",Recapture("scene_missing")),3=>Tool("get_inventory","{}"),_=>Answer("截图没显示的物品由即时背包查询核实。")};
}))) {
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"画面外还有什么物品",capture,processor,default,queries:gameQueries);
 Check(capture.Count==1&&gameQueries.Calls.Contains("get_inventory")&&requests.Count==4,"scene insufficiency can be resolved through real game data instead of forcing the player to change the image");
}
requests.Clear();gameQueries=new();capture=new(original,Frame(2));processor=new();
using(var http=new HttpClient(new Handler(async request=>{
 using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(body.RootElement.Clone());
 return requests.Count switch {1=>Tool("screen_crop",OneCrop),2=>Tool("screen_recapture",Recapture()),_=>Answer("已结合重截后的即时状态。")};
}))) {
 await new DeepSeekClient(new Secret(),http).AskScreenAsync(options,"重新看清楚",capture,processor,default,queries:gameQueries);
 string system=requests[^1].GetProperty("messages")[0].GetProperty("content").GetString()!;
 Check(gameQueries.Calls.Count==4&&system.Contains("OVERVIEW_2")&&!system.Contains("OVERVIEW_1"),"recapture refreshes overview and todos and replaces the old state snapshot");
}
Console.WriteLine($"All {passed} screen checks passed. No real key, network or game save used.");

sealed class Secret:ISecretStore{public Task<string> GetKeyAsync(CancellationToken token)=>Task.FromResult("sk-screen-test-credential");}
sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>handle(request);}
sealed class Capture(params ScreenFrame[] frames):IScreenCapture{
 public int Count;public ScreenFrame[] Frames=>frames;
 public Task<ScreenFrame> CaptureAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(frames[Math.Min(Count++,frames.Length-1)]);}
}
sealed class WaitingCapture:IScreenCapture{
 public TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public Task<ScreenFrame> CaptureAsync(CancellationToken token){Started.TrySetResult(true);return new TaskCompletionSource<ScreenFrame>().Task.WaitAsync(token);}
}
sealed class Images:IScreenImageProcessor{
 public int Previews;public List<(ScreenFrame Frame,ScreenRegion Region)> Crops=new();
 public ScreenImage Preview(ScreenFrame original){Previews++;return new(960,640,new byte[]{1,original.Rgba[0]},"preview");}
 public ScreenImage Crop(ScreenFrame original,ScreenRegion region){Crops.Add((original,region));return new(region.Width,region.Height,new byte[]{2,original.Rgba[0],(byte)Crops.Count},"crop");}
}

sealed class StateQueries:IGameQueries {
 public List<string> Calls=new();private int overviews;
 public Task<string> QueryAsync(string name,string arguments,CancellationToken token){
  token.ThrowIfCancellationRequested();Calls.Add(name);
  return Task.FromResult(JsonSerializer.Serialize(new{fixture=name switch {"get_overview"=>"OVERVIEW_"+(++overviews),"get_inventory"=>"CURRENT_INVENTORY_FIXTURE","get_quests"=>"CURRENT_QUEST_FIXTURE","get_todos"=>"CURRENT_TODO_FIXTURE",_=>"CURRENT_STATE"}}));
 }
}
