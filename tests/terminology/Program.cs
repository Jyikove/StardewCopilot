using System.Net;
using System.Text;
using System.Text.Json;
using StardewCodexChat;
using StardewCodexChat.Core;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
var glossary=new GameTerminology();
glossary.Replace(new[]{new GameTerm("Steel Falchion","钢刀"),new GameTerm("Crusader","十字军"),new GameTerm("Galaxy Sword","银河剑"),new GameTerm("Sword","剑"),new GameTerm("Templar's Blade","圣堂之刃"),new GameTerm("Duplicate","甲"),new GameTerm("Duplicate","乙")});
var terms=glossary.Current;
const string url="https://example.org/Steel%20Falchion?effect=Crusader";
string raw="老大，Steel Falchion 可以比较；Crusader 是附魔名。Galaxy Sword 和 Templar’s Blade["+url+"]。";
string expected="老大，钢刀 可以比较；十字军 是附魔名。银河剑 和 圣堂之刃["+url+"]。";
Check(terms.Rewrite(raw)==expected,"known names use exact supplied translations; URLs and other facts remain intact");
Check(terms.Rewrite("xSteel Falchion Steel Falchions Swordfish Duplicate Unknown Name")=="xSteel Falchion Steel Falchions Swordfish Duplicate Unknown Name","embedded identifiers, ambiguous names and unknown terms are not guessed");
Check(terms.Rewrite("STEEL FALCHION") == "钢刀","case variation resolves to the same official term");
foreach (var pair in new[] {
 ("Steel Falchion（钢刀）", "钢刀"), ("钢刀 (Steel Falchion)", "钢刀"),
 ("Steel Falchion (Steel Falchion)", "钢刀"), ("钢刀（钢刀）（钢刀）", "钢刀"),
 ("推荐钢刀 （ 钢刀 ）哦", "推荐钢刀哦"),
 ("钢刀（速度 +4），圣堂之刃（22–29伤害）", "钢刀（速度 +4），圣堂之刃（22–29伤害）"),
 ("钢刀（银河剑），提示（提示）", "钢刀（银河剑），提示（提示）"),
 ("https://example.org/钢刀(钢刀)", "https://example.org/钢刀(钢刀)") })
{
 Check(terms.Rewrite(pair.Item1)==pair.Item2,"duplicate names collapse but useful parentheses and URLs survive: "+pair.Item1);
 bool correct=true;
 for(int split=0;split<=pair.Item1.Length;split++){
  var output=new StringBuilder();var stream=terms.Stream(s=>output.Append(s));
  stream.Push(pair.Item1[..split]);stream.Push(pair.Item1[split..]);stream.Complete();
  if(output.ToString()!=pair.Item2)throw new Exception($"split={split}: {pair.Item1} => {output}");
  correct &= output.ToString()==pair.Item2;
 }
 var characters=new StringBuilder();var characterByCharacter=terms.Stream(s=>characters.Append(s));
 foreach(char c in pair.Item1)characterByCharacter.Push(c.ToString());characterByCharacter.Complete();
 Check(correct&&characters.ToString()==pair.Item2,"duplicate cleanup is independent of SSE chunk boundaries");
}
for(int split=0;split<=raw.Length;split++){
 var output=new StringBuilder();var stream=terms.Stream(s=>output.Append(s));
 stream.Push(raw[..split]);stream.Push(raw[split..]);stream.Complete();
 Check(output.ToString()==expected,"two-chunk boundary "+split);
}
var characterOutput=new StringBuilder();var characterStream=terms.Stream(s=>characterOutput.Append(s));
foreach(char c in raw)characterStream.Push(c.ToString());characterStream.Complete();
Check(characterOutput.ToString()==expected,"single-character SSE pieces preserve complete names and URLs");
var instant=new StringBuilder();var chineseStream=terms.Stream(s=>instant.Append(s));chineseStream.Push("老大，记得浇水哦！");
Check(instant.ToString()=="老大，记得浇水哦！","ordinary Chinese streams immediately without buffering a paragraph");
string context=terms.Context("钢刀 Crusader "+url);
Check(context.Contains("Steel Falchion = 钢刀")&&context.Contains("Crusader = 十字军")&&context.Contains("不证明物品持有"),"Chinese questions and English results get a name-only reference, not invented game facts");
var many=new GameTerminology.Snapshot(Enumerable.Range(0,100).Select(i=>new GameTerm("Fixture Term "+i,"合成名"+i)));
string bounded=many.Context(string.Join(" ",Enumerable.Range(0,100).Select(i=>"Fixture Term "+i)));
Check(bounded.Length<=1800&&bounded.Split(" = ").Length<=25,"only a bounded relevant glossary is sent to the model");
glossary.Replace(new[]{new GameTerm("Steel Falchion","模组自定义刀")});
Check(terms.Rewrite("Steel Falchion")=="钢刀"&&glossary.Current.Rewrite("Steel Falchion")=="模组自定义刀","a request snapshot stays stable while refreshed resources publish a new translation");
glossary.Replace(new[]{new GameTerm("Steel Falchion","钢刀"),new GameTerm("Crusader","十字军")});
var options=new ChatOptions();
HttpResponseMessage Reply(string text,bool stream)=>new(HttpStatusCode.OK){Content=new StringContent(stream
 ? "data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=text},finish_reason=(string?)null}}})+"\n\ndata: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=""},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n"
 : JsonSerializer.Serialize(new{choices=new[]{new{finish_reason="stop",message=new{role="assistant",content=text}}}}),Encoding.UTF8,stream?"text/event-stream":"application/json")};
HttpResponseMessage Tool(bool streaming)
{
 var calls=new[]{new{index=0,id="names",type="function",function=new{name="wiki_search",arguments="{\"query\":\"Steel Falchion\"}"}}};
 string data=streaming?"data: "+JsonSerializer.Serialize(new{choices=new[]{new{finish_reason="tool_calls",delta=new{tool_calls=calls}}}})+"\n\ndata: [DONE]\n\n"
  :JsonSerializer.Serialize(new{choices=new[]{new{finish_reason="tool_calls",message=new{role="assistant",content="",reasoning_content="",tool_calls=calls}}}});
 return new(HttpStatusCode.OK){Content=new StringContent(data,Encoding.UTF8,streaming?"text/event-stream":"application/json")};
}
foreach(bool research in new[]{false,true})foreach(bool streaming in new[]{false,true}){
 int requests=0,lookups=0;
 const string source="https://stardewvalleywiki.com/Steel_Falchion";
 string evidence=JsonSerializer.Serialize(new{cacheOnly=false,quality="High",chunks=new[]{new{url=source,stale=false,body="Steel Falchion; Crusader. Synthetic protocol fixture only."}}});
 using var http=new HttpClient(new Handler(async request=>{
  using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());var body=doc.RootElement;
  if(++requests==1)return Tool(streaming);
  var contents=body.GetProperty("messages").EnumerateArray().Where(m=>m.TryGetProperty("content",out var v)&&v.ValueKind==JsonValueKind.String).Select(m=>m.GetProperty("content").GetString()!).ToArray();
  Check(contents.Any(c=>c.Contains("Crusader = 十字军")),"new retrieval terms become available before the final answer");
  Check(contents.Contains(evidence),"retrieved evidence JSON is unchanged, separate from translation instructions");
  if(research)Check(!string.Join("",contents).Contains("old history"),"research glossary does not reintroduce conversation history");
  return Reply("Steel Falchion（钢刀）；Crusader（十字军）["+source+"]。",streaming);
 }));
 Task<string> Retrieve(string query,CancellationToken token){Check(query=="Steel Falchion","English search keywords are never rewritten");lookups++;return Task.FromResult(evidence);}
 var client=new DeepSeekClient(new Secret(),http,knowledge:Retrieve,uncachedKnowledge:Retrieve,terminology:glossary);
 var chunks=new StringBuilder();
 string answer=await client.AskAsync(options,new[]{new ChatMessage("system","fixture"),new ChatMessage("assistant","old history"),new ChatMessage("user","钢刀好用吗？")},default,streaming?s=>chunks.Append(s):null,new Queries(),research);
 Check(answer=="钢刀；十字军["+source+"]。"&&lookups==1&&requests==2,"normal/research and JSON/SSE normalize their final answer consistently");
 if(streaming)Check(chunks.ToString()==answer,"streamed translated text equals the saved final answer");
}
using(var screenHttp=new HttpClient(new Handler(async request=>{
 using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
 Check(json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("Steel Falchion = 钢刀"),"screen receives the same bounded game-name reference");
 return Reply("画面里是 Steel Falchion（钢刀）。",true);
}))){
 var output=new StringBuilder();
 string answer=await new DeepSeekClient(new Secret(),screenHttp,terminology:glossary).AskScreenAsync(options,"看看钢刀",new Capture(),new Images(),default,s=>output.Append(s));
 Check(answer=="画面里是 钢刀。"&&output.ToString()==answer,"screen output uses the same streaming name normalization");
}
using(var helpHttp=new HttpClient(new Handler(_=>Task.FromResult(Reply("Steel Falchion（钢刀）",false))))){
 string answer=await new DeepSeekClient(new Secret(),helpHttp,terminology:glossary).AskAsync(options,ModHelp.Messages("钢刀"),default);
 Check(answer=="钢刀","help uses the same player-facing name normalization");
}
string root=Path.Combine(Path.GetTempPath(),"copilot-terminology-"+Guid.NewGuid().ToString("N"));
try{
 using var http=new HttpClient(new Handler(_=>Task.FromResult(Reply("Steel Falchion 和 Crusader。",true))));
 var store=new ConversationStore(root);var session=new ChatSession(new DeepSeekClient(new Secret(),http,terminology:glossary),store:store);
 async Task Finish(){using var limit=new CancellationTokenSource(5000);while(session.IsBusy){await Task.Delay(5,limit.Token);session.Tick();}}
 var legacy=new Conversation();legacy.AddExchange("Steel Falchion user text","Steel Falchion（钢刀） 和 Crusader（十字军）。");await store.SaveAsync("legacy",legacy.Snapshot());
 byte[] legacyFile=await File.ReadAllBytesAsync(store.PathFor("legacy"));
 session.SwitchSave("legacy");await Finish();
 Check(session.Display[^1].Text=="钢刀 和 十字军。"&&session.Display[^2].Text=="Steel Falchion user text","restored assistant history is localized for display without rewriting player messages");
 Check(legacyFile.SequenceEqual(await File.ReadAllBytesAsync(store.PathFor("legacy"))),"opening old history does not rewrite its file");
 session.SwitchSave("synthetic-save");await Finish();session.Draft.Replace("钢刀呢？");session.Send(options);await Finish();
 var saved=(await store.LoadAsync("synthetic-save")).State;
 Check(saved.Transcript[^1].Text=="钢刀 和 十字军。"&&session.Display[^1].Text==saved.Transcript[^1].Text,"UI and per-save history both contain the translated response");
}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
Console.WriteLine($"All {passed} terminology checks passed. Synthetic responses; no real Key or network.");
sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> reply):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)=>reply(r);
}
sealed class Secret:ISecretStore {public Task<string> GetKeyAsync(CancellationToken token)=>Task.FromResult("synthetic-key-not-real");}
sealed class Queries:IGameQueries {public Task<string> QueryAsync(string name,string args,CancellationToken token)=>Task.FromResult("{\"worldReady\":true}");}
sealed class Capture:IScreenCapture {public Task<ScreenFrame> CaptureAsync(CancellationToken token)=>Task.FromResult(new ScreenFrame(4,4,new byte[64],DateTimeOffset.UtcNow));}
sealed class Images:IScreenImageProcessor {
 public ScreenImage Preview(ScreenFrame frame)=>new(4,4,new byte[]{1},"synthetic");
 public ScreenImage Crop(ScreenFrame frame,ScreenRegion region)=>throw new Exception("No crop expected");
}
