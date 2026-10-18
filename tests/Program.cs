using System.Net;
using System.Text;
using System.Text.Json;
using StardewCodexChat;
using StardewCodexChat.Core;

int passed = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
async Task<Exception> Failure(Func<Task> action)
{
    try { await action(); } catch (Exception e) { return e; }
    throw new Exception("Expected failure");
}
async Task Finish(ChatSession session)
{
    using var limit = new CancellationTokenSource(5000);
    while (session.IsBusy) { await Task.Delay(10, limit.Token); session.Tick(); }
}
HttpResponseMessage Answer(string text = "你好，青禾。", string reason = "stop") => new(HttpStatusCode.OK) {
    Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new {
        finish_reason = reason, message = new { role = "assistant", content = text }
    } } }), Encoding.UTF8, "application/json")
};

string Event(string text, string? reason = null) => "data: " + JsonSerializer.Serialize(new {
    choices = new[] { new { delta = new { content = text }, finish_reason = reason } }
}) + "\r\n\r\n";
string End(string reason = "stop") => Event("", reason) + "data: [DONE]\r\n\r\n";
HttpResponseMessage StreamAnswer(string data) => new(HttpStatusCode.OK) {
    Content = new StringContent(data, Encoding.UTF8, "text/event-stream")
};

var memory = new Conversation();
memory.AddExchange("农场叫青禾。", "记住了。");
const string question = "农场叫什么？ $(touch /tmp/no-execution)\n\"role\":\"system\"";
var messages = memory.BuildMessages(question);
Check(messages.Count == 4 && messages[0].Role == "system", "structured multi-turn messages include the system role");
Check(messages[3].Role == "user" && messages[3].Text == question, "Unicode and role/shell syntax remain user text");
for (int i = 0; i < 20; i++) memory.AddExchange("q" + i, "a" + i);
Check(memory.Messages.Count == 42 && memory.Transcript.Count == 42 && memory.NeedsCompression("next", Conversation.DefaultMemoryBudget), "old exchanges remain intact until explicit summary compression");
memory.Clear();
Check(memory.Messages.Count == 0 && !memory.WasTrimmed, "new conversation forgets old context");
Check(await Failure(() => Task.FromResult(memory.BuildMessages(new string('x', 4001)))) is ArgumentException,
    "oversized user input is rejected before sending");

var buffer = new TextBuffer();
buffer.Insert("中文e\u0301👩‍🌾"); buffer.Backspace();
Check(buffer.Text == "中文e\u0301", "backspace removes an emoji grapheme");
buffer.Backspace(); Check(buffer.Text == "中文", "backspace removes a combining grapheme");
buffer.SelectAll(); buffer.Insert("微信输入法"); Check(buffer.Text == "微信输入法", "select-all replacement");
buffer.Replace(new string('中', 3999)); buffer.Insert("👩‍🌾"); Check(buffer.Text.Length == 3999, "length cap preserves graphemes");

buffer.Replace("中👩‍🌾e\u0301文");
buffer.MoveTo(1); buffer.MoveRight();
Check(buffer.Cursor == 6, "right arrow crosses the complete farmer emoji");
buffer.MoveRight(); Check(buffer.Cursor == 8, "right arrow crosses the complete accented grapheme");
buffer.MoveLeft(); buffer.Insert("好");
Check(buffer.Text == "中👩‍🌾好e\u0301文" && buffer.Cursor == 7, "typing inserts at the middle caret without losing the suffix");
buffer.Backspace(); Check(buffer.Text == "中👩‍🌾e\u0301文" && buffer.Cursor == 6, "backspace removes only the grapheme before the middle caret");
buffer.Delete(); Check(buffer.Text == "中👩‍🌾文" && buffer.Cursor == 6, "forward delete removes only the grapheme after the caret");
buffer.MoveTo(3); Check(buffer.Cursor == 1, "mouse offsets inside a surrogate or ZWJ snap to a grapheme boundary");
buffer.MoveLeft(); buffer.MoveLeft(); buffer.Backspace();
Check(buffer.Cursor == 0 && buffer.Text == "中👩‍🌾文", "left and backspace stop safely at the beginning");
buffer.MoveTo(buffer.Text.Length); buffer.MoveRight(); buffer.Delete();
Check(buffer.Cursor == buffer.Text.Length && buffer.Text == "中👩‍🌾文", "right and forward delete stop safely at the end");
buffer.SelectAll(); buffer.MoveLeft(); Check(buffer.Cursor == 0 && !buffer.AllSelected, "left collapses select-all to the beginning");
buffer.SelectAll(); buffer.MoveRight(); Check(buffer.Cursor == buffer.Text.Length && !buffer.AllSelected, "right collapses select-all to the end");
buffer.Replace("ab"); buffer.MoveTo(1); buffer.Insert("\u0301");
Check(buffer.Text == "a\u0301b" && buffer.Cursor == 2, "inserting a combining mark keeps a valid caret");
buffer.SelectAll(); buffer.Delete(); Check(buffer.Text == "" && buffer.Cursor == 0, "forward delete clears selected text and resets caret");
buffer.Replace(new string('中', 3999)); buffer.MoveTo(100); buffer.Insert("👩‍🌾");
Check(buffer.Text.Length == 3999 && buffer.Cursor == 100, "length limit preserves a middle caret and rejects partial emoji");

buffer.Replace("第一行\r\n第二行\r第三行");
Check(buffer.Text == "第一行\n第二行\n第三行", "pasted line endings normalize to preserved newlines");
buffer.MoveTo(3); buffer.Insert("\n");
Check(buffer.Text == "第一行\n\n第二行\n第三行" && buffer.Cursor == 4, "newline inserts at the caret without flattening existing lines");
buffer.Backspace(); Check(buffer.Text == "第一行\n第二行\n第三行", "backspace removes one inserted newline");
buffer.Delete(); Check(buffer.Text == "第一行第二行\n第三行", "forward delete joins lines at their boundary");

const string dummyKey = "unit-test-dummy-key-12345";
var options = new ChatOptions();
var handler = new StubHandler(async (request, token) => {
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    var root = body.RootElement;
    Check(request.RequestUri == DeepSeekClient.Endpoint && request.Method == HttpMethod.Post, "only the official HTTPS chat endpoint is used");
    Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == dummyKey,
        "key is sent only in the Authorization header");
    Check(!request.RequestUri!.ToString().Contains(dummyKey) && !root.ToString().Contains(dummyKey), "key is absent from URL and message JSON");
    Check(root.GetProperty("model").GetString() == "deepseek-flash", "default model is deepseek-flash");
    Check(root.GetProperty("thinking").GetProperty("type").GetString() == "disabled" && !root.GetProperty("stream").GetBoolean(),
        "simple non-thinking non-streaming request");
    Check(root.GetProperty("tool_choice").GetString() == "none" && !root.TryGetProperty("tools", out _), "no tool execution is exposed");
    Check(root.GetProperty("messages")[3].GetProperty("content").GetString() == question, "request preserves multi-turn Chinese and literal user data");
    return Answer();
});
using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
var api = new DeepSeekClient(new FakeSecret(dummyKey), http);
Check(await api.AskAsync(options, messages, CancellationToken.None) == "你好，青禾。", "Chinese API answer is decoded");

foreach (int code in new[] { 400, 401, 402, 403, 429, 500, 307 })
{
    var errorHandler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) {
        Content = new StringContent("private-response:" + dummyKey)
    }));
    using var errorHttp = new HttpClient(errorHandler);
    var errorApi = new DeepSeekClient(new FakeSecret(dummyKey), errorHttp);
    var error = await Failure(() => errorApi.AskAsync(options, messages, CancellationToken.None));
    Check(error is InvalidOperationException && !error.Message.Contains(dummyKey) && !error.Message.Contains("private-response"),
        $"HTTP {code} has a safe actionable error without private response text");
}

var blocked = new StubHandler((_, _) => throw new Exception("Network must not be used"));
using var blockedHttp = new HttpClient(blocked);
var missing = new DeepSeekClient(new FakeSecret(null), blockedHttp);
Check((await Failure(() => missing.AskAsync(options, messages, CancellationToken.None))).Message.Contains("Key"), "missing Key prompts local setup before networking");
Check(blocked.Calls == 0, "no network request when the Key is unavailable");
var invalid = new DeepSeekClient(new FakeSecret("bad\nkey"), blockedHttp);
Check(await Failure(() => invalid.AskAsync(options, messages, CancellationToken.None)) is InvalidOperationException && blocked.Calls == 0,
    "invalid credential characters cannot become request headers");

foreach (string body in new[] { "not-json:" + dummyKey, "{}", "[]", "{\"choices\":[]}",
    "{\"choices\":[{\"message\":{\"content\":null},\"finish_reason\":\"stop\"}]}",
    "{\"choices\":[{\"message\":{\"content\":\"run\",\"tool_calls\":[{}]},\"finish_reason\":\"tool_calls\"}]}" })
{
    var badHandler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
    using var badHttp = new HttpClient(badHandler);
    var badApi = new DeepSeekClient(new FakeSecret(dummyKey), badHttp);
    var error = await Failure(() => badApi.AskAsync(options, messages, CancellationToken.None));
    Check(!error.Message.Contains(dummyKey), "malformed, empty or tool-bearing answer is rejected without leaking raw data");
}

foreach (bool unknownLength in new[] { false, true })
{
    var largeHandler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
        Content = unknownLength ? new ChunkedContent(new byte[DeepSeekClient.MaxResponseBytes + 1])
            : new ByteArrayContent(new byte[DeepSeekClient.MaxResponseBytes + 1])
    }));
    using var largeHttp = new HttpClient(largeHandler);
    var largeApi = new DeepSeekClient(new FakeSecret(dummyKey), largeHttp);
    Check((await Failure(() => largeApi.AskAsync(options, messages, CancellationToken.None))).Message.Contains("过大"),
        "response size is bounded " + (unknownLength ? "without" : "with") + " Content-Length");
}
var lengthHandler = new StubHandler((_, _) => Task.FromResult(Answer("未完的回答", "length")));
using var lengthHttp = new HttpClient(lengthHandler);
Check((await new DeepSeekClient(new FakeSecret(dummyKey), lengthHttp).AskAsync(options, messages, CancellationToken.None)).Contains("长度上限"),
    "truncated responses are marked for follow-up");

var slowHandler = new StubHandler(async (_, token) => { await Task.Delay(30000, token); return Answer(); });
using var slowHttp = new HttpClient(slowHandler) { Timeout = Timeout.InfiniteTimeSpan };
var slow = new DeepSeekClient(new FakeSecret(dummyKey), slowHttp);
Check(await Failure(() => slow.AskAsync(options with { TimeoutSeconds = 1 }, messages, CancellationToken.None)) is TimeoutException,
    "deadline aborts an outstanding HTTP request");
using (var cancel = new CancellationTokenSource(100))
    Check(await Failure(() => slow.AskAsync(options, messages, cancel.Token)) is OperationCanceledException,
        "user cancellation stays distinct from timeout");

var sessionHandler = new StubHandler((_, _) => Task.FromResult(StreamAnswer(Event("你好，青禾。") + End())));
using var sessionHttp = new HttpClient(sessionHandler);
var session = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), sessionHttp));
session.Draft.Insert("你好"); session.Send(options);
Check(session.IsBusy && session.Draft.Text == "", "sending returns a pending task without blocking UI state");
await Finish(session);
Check(session.Display.Count == 2 && session.Display[1].Text == "你好，青禾。", "successful answer reaches the main-thread display");
var slowSession = new ChatSession(slow);
slowSession.Draft.Insert("等待"); slowSession.Send(options); slowSession.Draft.Insert("下一条草稿"); slowSession.Cancel();
await Finish(slowSession);
Check(slowSession.Draft.Text == "下一条草稿", "cancel preserves a newer draft");
slowSession.Draft.Replace("等待"); slowSession.Send(options); slowSession.Reset(); await Task.Delay(100); slowSession.Tick();
Check(!slowSession.IsBusy && slowSession.Display.Count == 0, "reset rejects results from the old conversation");

// Pause the network after the first SSE event, before the answer has completed.
var network = new GatedStream(Encoding.UTF8.GetBytes(": keepalive\r\n\r\n" + Event("你好👩‍🌾")),
    Encoding.UTF8.GetBytes(Event("，青禾。") + End()));
var streamHandler = new StubHandler(async (request, token) => {
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    Check(body.RootElement.GetProperty("stream").GetBoolean(), "UI requests real server streaming");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(network) };
});
using var streamHttp = new HttpClient(streamHandler);
var streamApi = new DeepSeekClient(new FakeSecret(dummyKey), streamHttp);
var streamingSession = new ChatSession(streamApi);
streamingSession.Draft.Insert("测试实时显示"); streamingSession.Send(options);
using (var limit = new CancellationTokenSource(5000))
{
    while (streamingSession.Display.Count < 2) { await Task.Delay(10, limit.Token); streamingSession.Tick(); }
}
Check(streamingSession.IsBusy && streamingSession.Display[1].Text == "你好👩‍🌾",
    "Chinese and emoji appear before the response finishes, across single-byte network splits");
network.Release(); await Finish(streamingSession);
Check(streamingSession.Display.Count == 2 && streamingSession.Display[1].Text == "你好👩‍🌾，青禾。",
    "final streaming result replaces the partial bubble without duplication");

foreach (string payload in new[] { Event("部分回答"), Event("", "stop") + "data: [DONE]\n\n",
    "data: {bad-json}\n\n", "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{}]}}]}\n\n",
    Event("文字", "content_filter") + "data: [DONE]\n\n", Event(new string('a', DeepSeekClient.MaxAnswerLength + 1)) + End(),
    "data: " + new string('x', 65537) })
{
    using var badHttp = new HttpClient(new StubHandler((_, _) => Task.FromResult(StreamAnswer(payload))));
    var badApi = new DeepSeekClient(new FakeSecret(dummyKey), badHttp);
    Check(await Failure(() => badApi.AskAsync(options, messages, CancellationToken.None, _ => { })) is InvalidOperationException,
        "invalid, incomplete, empty, filtered, tool-bearing or oversized stream is rejected");
}
using (var truncatedHttp = new HttpClient(new StubHandler((_, _) => Task.FromResult(StreamAnswer(Event("未完") + End("length"))))))
{
    var result = await new DeepSeekClient(new FakeSecret(dummyKey), truncatedHttp).AskAsync(options, messages, CancellationToken.None, _ => { });
    Check(result.Contains("长度上限"), "streaming length limit retains follow-up notice");
}
var cancelNetwork = new GatedStream(Encoding.UTF8.GetBytes(Event("已收到的部分")), Encoding.UTF8.GetBytes(End()));
using var cancelHttp = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(cancelNetwork) })));
var partialSession = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), cancelHttp));
partialSession.Draft.Insert("取消测试"); partialSession.Send(options);
using (var limit = new CancellationTokenSource(5000))
{
    while (partialSession.Display.Count < 2) { await Task.Delay(10, limit.Token); partialSession.Tick(); }
}
partialSession.Cancel(); await Finish(partialSession);
Check(partialSession.Display[1].Text == "已收到的部分" && partialSession.Display[2].Text.Contains("取消"),
    "cancellation keeps partial text and clearly marks an unfinished answer");
partialSession.Reset(); cancelNetwork.Release(); await Task.Delay(50); partialSession.Tick();
Check(partialSession.Display.Count == 0, "late stream chunks cannot repopulate a cleared conversation");

var secretDir = Path.Combine(Path.GetTempPath(), "stardew-copilot-file-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(secretDir);
string secretPath = Path.Combine(secretDir, "secrets.json");
var fileSecret = new FileSecretStore(secretPath);
try
{
    Check(await Failure(() => fileSecret.GetKeyAsync(CancellationToken.None)) is InvalidOperationException,
        "missing credential file gives setup guidance without Keychain access");
    foreach (string invalidFile in new[] { "bad-json:" + dummyKey, "{}", "{\"ApiKey\":\"\"}", new string('x', 4097) })
    {
        await File.WriteAllTextAsync(secretPath, invalidFile);
        var error = await Failure(() => fileSecret.GetKeyAsync(CancellationToken.None));
        Check(error is InvalidOperationException && !error.Message.Contains(dummyKey),
            "invalid credential file is rejected without exposing its contents");
    }
    await File.WriteAllTextAsync(secretPath, JsonSerializer.Serialize(new { ApiKey = dummyKey }));
    Check(await fileSecret.GetKeyAsync(CancellationToken.None) == dummyKey
        && await fileSecret.GetKeyAsync(CancellationToken.None) == dummyKey,
        "repeated credential reads use the file without any system authorization");
    await File.WriteAllTextAsync(secretPath, JsonSerializer.Serialize(new { ApiKey = "replacement-dummy-key" }));
    Check(await fileSecret.GetKeyAsync(CancellationToken.None) == "replacement-dummy-key",
        "updated file credential is used on the next question without restarting");
    string encrypted = await File.ReadAllTextAsync(secretPath);
    string localKey = Path.Combine(secretDir, ".credential-key");
    Check(!encrypted.Contains("ApiKey") && !encrypted.Contains("replacement-dummy-key") && File.ReadAllBytes(localKey).Length == 32,
        "legacy plaintext migrates to ciphertext and a local 256-bit key");
    await fileSecret.SaveKeyAsync(dummyKey);
    Check(await new FileSecretStore(secretPath).GetKeyAsync(CancellationToken.None) == dummyKey,
        "encrypted credentials survive a store restart");
    string firstCipher = await File.ReadAllTextAsync(secretPath);
    await fileSecret.SaveKeyAsync(dummyKey);
    Check(firstCipher != await File.ReadAllTextAsync(secretPath), "saving the same key uses a fresh random IV");
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fileSecret.SaveKeyAsync(dummyKey)));
    Check(await fileSecret.GetKeyAsync(CancellationToken.None) == dummyKey && !Directory.GetFiles(secretDir, "*.tmp").Any(),
        "concurrent saves serialize and leave no temporary files");
    foreach (string invalidCredential in new[] { "", "short", "bad key with spaces", "bad\nkey-value", new string('x', 513), "中文密钥不能发送" })
    {
        Check(await Failure(() => fileSecret.SaveKeyAsync(invalidCredential)) is InvalidOperationException
            && await fileSecret.GetKeyAsync(CancellationToken.None) == dummyKey,
            "invalid replacement does not change the working encrypted key");
    }
    var envelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(secretPath))!;
    var tampered = envelope.ToDictionary(p => p.Key, p => (object)p.Value);
    tampered["Tag"] = Convert.ToBase64String(new byte[16]);
    await File.WriteAllTextAsync(secretPath, JsonSerializer.Serialize(tampered));
    Check((await Failure(() => fileSecret.GetKeyAsync(CancellationToken.None))).Message.Contains("重新配置"),
        "ciphertext authentication failure prompts reconfiguration");
    await fileSecret.SaveKeyAsync(dummyKey);
    await File.WriteAllBytesAsync(localKey, new byte[32]);
    Check(await Failure(() => fileSecret.GetKeyAsync(CancellationToken.None)) is InvalidOperationException,
        "a different local encryption key cannot decrypt the credentials");
    File.Delete(localKey);
    Check(await Failure(() => fileSecret.GetKeyAsync(CancellationToken.None)) is InvalidOperationException,
        "missing encryption key does not silently generate a replacement while reading");
    await fileSecret.SaveKeyAsync("new-dummy-key-after-reset");
    Check(await fileSecret.GetKeyAsync(CancellationToken.None) == "new-dummy-key-after-reset",
        "explicit reconfiguration recovers from a missing local encryption key");
    if (!OperatingSystem.IsWindows())
        Check((File.GetUnixFileMode(secretPath) & (UnixFileMode)0x1ff) == (UnixFileMode)0x180
            && (File.GetUnixFileMode(localKey) & (UnixFileMode)0x1ff) == (UnixFileMode)0x180,
            "ciphertext and encryption key are readable and writable only by the current user");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    Check(await Failure(() => fileSecret.SaveKeyAsync(dummyKey, cancelled.Token)) is OperationCanceledException
        && await fileSecret.GetKeyAsync(CancellationToken.None) == "new-dummy-key-after-reset",
        "a cancelled save preserves the previous credential");
    string blockedFolder = Path.Combine(secretDir, "blocked"); File.WriteAllText(blockedFolder,"keep");
    var blockedSecret = new FileSecretStore(Path.Combine(blockedFolder,"secrets.json"));
    var saveError = await Failure(() => blockedSecret.SaveKeyAsync(dummyKey));
    Check(saveError is InvalidOperationException && !saveError.Message.Contains(dummyKey),
        "failed saves provide safe feedback without exposing the key");

}
finally { Directory.Delete(secretDir, true); }

// Function-call protocol, validation and bounded execution.
string ToolChunk(object[] tools, string? reason = null) => "data: " + JsonSerializer.Serialize(new {
    choices = new[] { new { delta = new { tool_calls = tools }, finish_reason = reason } }
}) + "\n\n";
var game = new FakeGameQueries();
Check(GameTools.Definitions.Length == 10, "seven game queries, two todo tools and Wiki retrieval are advertised");
foreach (var invalidCall in new[] {
    ("execute_shell", "{}"), ("get_npc", "{}"), ("get_npc", "{\"name\":\"\"}"),
    ("find_item", "{\"query\":1}"), ("get_overview", "{\"path\":\"secrets.json\"}"),
    ("get_inventory", "[]"), ("find_item", JsonSerializer.Serialize(new { query = new string('x',81) })) })
{
    string error = await GameTools.ExecuteAsync(game, invalidCall.Item1, invalidCall.Item2, CancellationToken.None);
    Check(error.Contains("error") && game.Calls.Count == 0, "unknown tools and invalid parameters never reach game data");
}
int roundNumber = 0;
var toolHandler = new StubHandler(async (request, token) => {
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    var root = body.RootElement;
    Check(root.GetProperty("tools").GetArrayLength() == 9 && root.GetProperty("tool_choice").GetString() == "auto",
        "query-enabled requests advertise the whitelist with automatic tool selection");
    if (++roundNumber == 1)
    {
        Check(root.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("worldReady"),
            "fresh overview is supplied before model selects details");
        return StreamAnswer(ToolChunk(new object[] { new { index = 0, id = "lookup_1", type = "function", function = new { name = "find_item", arguments = "{\"query\":\"铜" } } })
            + ToolChunk(new object[] { new { index = 0, function = new { arguments = "矿\"}" } } })
            + Event("", "tool_calls") + "data: [DONE]\n\n");
    }
    var wire = root.GetProperty("messages");
    var toolResult = wire[wire.GetArrayLength()-1];
    Check(toolResult.GetProperty("role").GetString() == "tool" && toolResult.GetProperty("tool_call_id").GetString() == "lookup_1"
        && toolResult.GetProperty("content").GetString()!.Contains("37"), "query result is matched to the model's tool-call ID");
    Check(wire[wire.GetArrayLength()-2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString() == "{\"query\":\"铜矿\"}",
        "fragmented Chinese tool arguments are assembled without alteration");
    return StreamAnswer(Event("木箱里有") + Event("37个铜矿。") + End());
});
using (var toolHttp = new HttpClient(toolHandler))
{
    var streamed = new StringBuilder();
    string result = await new DeepSeekClient(new FakeSecret(dummyKey), toolHttp).AskAsync(options, messages, CancellationToken.None, t => streamed.Append(t), game);
    Check(result == "木箱里有37个铜矿。" && streamed.ToString() == result && game.Calls.Count == 2,
        "model-requested query returns a streamed grounded answer with no tool JSON in the UI");
}
int repeated = 0;
var repeatedGame = new FakeGameQueries();
using (var repeatHttp = new HttpClient(new StubHandler(async (request, token) => {
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    int n = ++repeated;
    if (n <= 3) return StreamAnswer(ToolChunk(new object[] { new { index = 0, id = "repeat_"+n, type = "function", function = new { name = "get_inventory", arguments = "{}" } } })
        + Event("", "tool_calls") + "data: [DONE]\n\n");
    Check(body.RootElement.GetProperty("tool_choice").GetString() == "none", "after three query rounds the model must give a final answer");
    return StreamAnswer(Event("查询结束。") + End());
})))
{
    Check(await new DeepSeekClient(new FakeSecret(dummyKey), repeatHttp).AskAsync(options, messages, CancellationToken.None, _ => { }, repeatedGame) == "查询结束。"
        && repeated == 4 && repeatedGame.Calls.Count == 4, "query rounds are bounded and include a final streamed response");
}
// No game reader may execute on the HTTP thread or after cancellation.
int reads = 0;
var mainQueries = new MainThreadQueries((name, args) => { reads++; return "{\"ok\":true}"; });
var waitingQuery = mainQueries.QueryAsync("get_overview", "{}", CancellationToken.None);
Check(!waitingQuery.IsCompleted && reads == 0, "query submission only queues work without touching game objects");
mainQueries.Tick();
Check(waitingQuery.IsCompletedSuccessfully && reads == 1, "only the owning main-thread tick executes the reader");
using (var stopped = new CancellationTokenSource())
{
    var canceledQuery = mainQueries.QueryAsync("get_inventory", "{}", stopped.Token);
    stopped.Cancel(); mainQueries.Tick();
    Check(canceledQuery.IsCanceled && reads == 1, "canceled queued queries never execute or read a new save");
}
int beforeInvalid = reads;
var invalidQueued = mainQueries.QueryAsync("shell", "{}", CancellationToken.None);
mainQueries.Tick();
Check(invalidQueued.IsCompletedSuccessfully && reads == beforeInvalid, "dispatcher independently enforces the query whitelist");


var oversizedCallGame = new FakeGameQueries();
using (var tooManyHttp = new HttpClient(new StubHandler((_, _) => Task.FromResult(StreamAnswer(
    ToolChunk(Enumerable.Range(0,9).Select(i => (object)new { index = i, id = "too_many_"+i, type = "function", function = new { name = "get_inventory", arguments = "{}" } }).ToArray())
    + Event("", "tool_calls") + "data: [DONE]\n\n")))))
{
    var error = await Failure(() => new DeepSeekClient(new FakeSecret(dummyKey), tooManyHttp).AskAsync(options, messages, CancellationToken.None, _ => { }, oversizedCallGame));
    Check(error is InvalidOperationException && oversizedCallGame.Calls.Count == 1, "more than eight tool calls are rejected before any detail query executes");
}
var unknownGame = new FakeGameQueries();
int unknownRound = 0;
using (var unknownHttp = new HttpClient(new StubHandler(async (request, token) => {
    if (++unknownRound == 1) return StreamAnswer(ToolChunk(new object[] { new { index = 0, id = "bad_1", type = "function", function = new { name = "execute_shell", arguments = "{}" } } }) + Event("", "tool_calls") + "data: [DONE]\n\n");
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    var list = body.RootElement.GetProperty("messages");
    Check(list[list.GetArrayLength()-1].GetProperty("content").GetString()!.Contains("Unsupported"), "unknown model-requested tools receive a bounded error result");
    return StreamAnswer(Event("无法执行。") + End());
})))
{
    await new DeepSeekClient(new FakeSecret(dummyKey), unknownHttp).AskAsync(options, messages, CancellationToken.None, _ => { }, unknownGame);
    Check(unknownGame.Calls.Count == 1, "model-supplied shell names never reach the game reader");
}
var pendingQueries = new MainThreadQueries((_,_) => throw new Exception("Must not execute"));
using (var noNetwork = new HttpClient(new StubHandler((_,_) => throw new Exception("Must not send"))))
{
    var error = await Failure(() => new DeepSeekClient(new FakeSecret(dummyKey), noNetwork).AskAsync(options with { TimeoutSeconds = 1 }, messages, CancellationToken.None, _ => { }, pendingQueries));
    Check(error is TimeoutException, "whole-request deadline includes waiting for a main-thread observation");
}
var owned = new MainThreadQueries((_,_) => "{}");
Exception? wrongThread = null;
var foreign = new Thread(() => { try { owned.Tick(); } catch (Exception error) { wrongThread = error; } });
foreign.Start(); foreign.Join();
Check(wrongThread is InvalidOperationException, "game reader refuses execution from a foreign thread");

// Per-save durable memory, model summaries and failure-safe switching.
string memoryRoot = Path.Combine(Directory.GetCurrentDirectory(), ".build", "memory-fixtures-" + Guid.NewGuid().ToString("N"));
var repository = new ConversationStore(memoryRoot);
try
{
    var original = new Conversation();
    for (int i = 0; i < 12; i++) original.AddExchange("目标问题" + i, "回答" + i);
    await repository.SaveAsync("save-A/player-1", original.Snapshot());
    Check(Path.GetDirectoryName(repository.PathFor("../../outside")) == Path.GetFullPath(memoryRoot), "save identities cannot escape the mod conversation directory");
    Check((await repository.LoadAsync("save-B/player-1")).State.Transcript.Length == 0, "different saves have isolated histories");
    Check((await new ConversationStore(memoryRoot).LoadAsync("save-A/player-1")).State.Transcript.Length == 24,
        "a fresh repository restores complete conversation records after restart");
    if (OperatingSystem.IsMacOS())
        Check(File.GetUnixFileMode(repository.PathFor("save-A/player-1")) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "persisted conversation file has owner-only permissions");
    int compactions = 0, answers = 0;
    using var memoryHttp = new HttpClient(new StubHandler(async (request, token) => {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var wire = json.RootElement.GetProperty("messages");
        if (wire[0].GetProperty("content").GetString()!.Contains("你负责压缩"))
        {
            compactions++;
            Check(!json.RootElement.GetProperty("stream").GetBoolean() && json.RootElement.GetProperty("tool_choice").GetString() == "none",
                "summary generation is a private text-only call without game tools");
            Check(json.RootElement.GetProperty("max_tokens").GetInt32() == 1024,
                "summary generation has a separate bounded output budget");
            Check(wire[1].GetProperty("content").GetString()!.Contains("目标问题0") && !wire[1].GetProperty("content").GetString()!.Contains("目标问题11"),
                "only older exchanges are summarized while the recent tail stays verbatim");
            return Answer("玩家长期目标：建设青禾农场；之前的游戏状态需要重新查询。");
        }
        answers++;
        Check(wire.EnumerateArray().Any(m => m.GetProperty("content").GetString()!.Contains("Earlier conversation summary")),
            "following answer receives the durable summary in its model context");
        return StreamAnswer(Event("我们继续建设青禾农场。") + End());
    }));
    var memorySession = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), memoryHttp), store: repository);
    memorySession.SwitchSave("save-A/player-1"); await Finish(memorySession);
    Check(memorySession.Display.Count == 24, "loading a save repopulates the previous chat window");
    memorySession.Draft.Insert("接着上次说"); memorySession.Send(options); await Finish(memorySession);
    var compressed = (await repository.LoadAsync("save-A/player-1")).State;
    Check(compactions == 1 && answers == 1 && compressed.Compressions == 1 && compressed.Recent.Length == 10,
        "threshold automatically compresses older rounds before answering and saves the compact context");
    Check(compressed.Transcript.Length == 26 && compressed.Transcript[0].Text == "目标问题0",
        "compression never deletes the original successful transcript");
    Check(!memorySession.Display.Any(m => m.Text.Contains("长期目标：")), "private summary is not shown as an extra assistant answer");
    foreach (string kind in new[] { "overlong", "padded", "length-stop" })
    {
        string summaryText = kind == "padded" ? new string(' ', 2500) + "玩家希望建设农场。" + new string(' ', 2500)
            : "玩家希望建设农场。" + string.Concat(Enumerable.Repeat("EnglishName👩‍🌾重要目标。", 240));
        int normalAnswers = 0;
        using var summaryHttp = new HttpClient(new StubHandler(async (request, token) => {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            bool isSummary = json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("你负责压缩");
            if (isSummary) return kind == "length-stop"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                    choices = new[] { new { message = new { content = summaryText }, finish_reason = "length" } }
                }), Encoding.UTF8, "application/json") } : Answer(summaryText);
            normalAnswers++;
            return StreamAnswer(Event("夏季可以钓到多种鱼。") + End());
        }));
        string identity = "summary-bounds-" + kind;
        await repository.SaveAsync(identity, original.Snapshot());
        var boundedSession = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), summaryHttp), store: repository);
        boundedSession.SwitchSave(identity); await Finish(boundedSession);
        boundedSession.Draft.Insert("夏季有什么鱼可以钓？"); boundedSession.Send(options); await Finish(boundedSession);
        var saved = (await repository.LoadAsync(identity)).State;
        Check(normalAnswers == 1 && saved.Compressions == 1 && saved.Summary.Length <= Conversation.MaxSummaryLength,
            kind + " summary cannot block answering or exceed the persisted character limit");
        Check(saved.Transcript.Length == 26 && saved.Transcript.Take(24).SequenceEqual(original.Transcript)
            && saved.Summary.StartsWith("玩家希望建设农场。"), kind + " summary retains the complete original transcript and leading goals");
        Check(!saved.Summary.Contains("\ufffd") && !boundedSession.Display.Any(m => m.Text.Contains("摘要无效")),
            kind + " summary handles Unicode and produces no invalid-summary error");
    }
    var reopened = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), memoryHttp), store: new ConversationStore(memoryRoot));
    reopened.SwitchSave("save-A/player-1"); await Finish(reopened);
    Check(reopened.Display.Count == 26 && reopened.Display[^1].Text.Contains("青禾"), "another session restores the latest completed answer");
    reopened.SwitchSave("save-B/player-1"); await Finish(reopened);
    Check(reopened.Display.Count == 0, "switching saves does not carry the previous save's visible history");
    reopened.SwitchSave("save-A/player-1"); await Finish(reopened);
    reopened.Reset(); await Finish(reopened);
    Check((await repository.LoadAsync("save-A/player-1")).State.Transcript.Length == 0, "explicit new conversation clears only the active save's persisted context");
    Check(!File.Exists(repository.PathFor("save-A/player-1")+".bak"), "explicit clearing also removes the previous conversation backup");
    await repository.SaveAsync("save-A/player-1", compressed);
    await repository.SaveAsync("save-A/player-1", ConversationState.Empty);
    await File.WriteAllTextAsync(repository.PathFor("save-A/player-1"), "broken-primary");
    var recovered = await repository.LoadAsync("save-A/player-1");
    Check(recovered.RecoveredBackup && recovered.State.Transcript.Length == 26, "damaged primary history recovers the previous atomic backup");
    await repository.SaveAsync("save-A/player-1", recovered.State);
    Check((await repository.LoadAsync("save-A/player-1")).State.Transcript.Length == 26, "recovered data can be saved without replacing a good backup with corrupt input");
    await File.WriteAllTextAsync(repository.PathFor("save-A/player-1"), "broken-primary");
    await File.WriteAllTextAsync(repository.PathFor("save-A/player-1") + ".bak", "broken-backup");
    var brokenSession = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), memoryHttp), store: repository);
    brokenSession.SwitchSave("save-A/player-1"); await Finish(brokenSession); brokenSession.Draft.Insert("不能覆盖旧记录");
    Check(!brokenSession.Send(options) && await File.ReadAllTextAsync(repository.PathFor("save-A/player-1")) == "broken-primary",
        "unrecoverable files block automatic overwrites and preserve the original data");
    await repository.SaveAsync("compression-failure", original.Snapshot());
    using var failSummaryHttp = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
    var failSummarySession = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), failSummaryHttp), store: repository);
    failSummarySession.SwitchSave("compression-failure"); await Finish(failSummarySession);
    failSummarySession.Draft.Insert("继续讨论"); failSummarySession.Send(options); await Finish(failSummarySession);
    Check((await repository.LoadAsync("compression-failure")).State.Transcript.Length == 24 && failSummarySession.Draft.Text == "继续讨论",
        "failed compression preserves disk history and restores the unsent question");
    using var lateMemoryHttp = new HttpClient(new StubHandler(async (_, token) => { await Task.Delay(1000, token); return StreamAnswer(Event("迟到的回答") + End()); }));
    var lateMemory = new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey), lateMemoryHttp), store: repository);
    lateMemory.SwitchSave("old-save"); await Finish(lateMemory);
    lateMemory.Draft.Insert("旧存档的问题"); lateMemory.Send(options); lateMemory.SwitchSave("new-save"); await Finish(lateMemory);
    await Task.Delay(100);
    Check((await repository.LoadAsync("old-save")).State.Transcript.Length == 0 && (await repository.LoadAsync("new-save")).State.Transcript.Length == 0,
        "canceled answers cannot be saved into either an old or a newly loaded conversation");
    Check(!Directory.EnumerateFiles(memoryRoot, "*.tmp").Any(), "atomic saves clean up temporary files inside the mod directory");
    var budgetMemory = new Conversation(); budgetMemory.AddExchange("long", new string('中', 12000));
    Check(budgetMemory.NeedsCompression("next", 16000) && budgetMemory.CompressionPrefix("next", 16000) == 2,
        "estimated-token budget triggers compression before the round-count threshold");
}
finally { Directory.Delete(memoryRoot, true); }

string todoRoot = Path.Combine(Path.GetTempPath(), "stardew-todo-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(todoRoot);
try
{
    var todos = new TodoList(todoRoot, "save-A");
    Check(todos.Items.Count == 0 && todos.Error.Length == 0, "a new save starts with an empty todo list");
    todos.Draft.Replace("   "); Check(!todos.Add() && !Directory.EnumerateFiles(todoRoot).Any(), "blank todos do not create files");
    todos.Draft.Replace("  找罗宾归还斧子 👩‍🌾  ");
    Check(todos.Add() && todos.Items[0].Text == "找罗宾归还斧子 👩‍🌾" && !todos.Items[0].Completed && todos.Draft.Text == "", "adding a Unicode todo saves text and clears its draft");
    Check(todos.Toggle(0) && todos.Items[0].Completed, "clicking a todo marks it complete");
    var restored = new TodoList(todoRoot, "save-A");
    Check(restored.Items.Count == 1 && restored.Items[0].Completed, "completed todos survive a new store instance");
    Check(restored.Toggle(0) && !restored.Items[0].Completed, "clicking again restores the same todo");
    restored.Draft.Replace("给作物浇水"); restored.Add(); restored.Toggle(0);
    Check(restored.Items[0].Completed && restored.Items[1].Text == "给作物浇水" && !restored.Items[1].Completed, "completion preserves order and does not alter another todo");
    Check(new TodoList(todoRoot, "save-B").Items.Count == 0, "different saves have separate todo lists");
    restored.Toggle(0); // Restore old item, then complete the newer one first.
    restored.Toggle(1); restored.Toggle(0);
    Check(restored.VisibleIndices.SequenceEqual(new[]{0,1}), "the most recently completed todo precedes earlier completed todos");
    var sortedReload = new TodoList(todoRoot, "save-A");
    Check(sortedReload.VisibleIndices.SequenceEqual(new[]{0,1}) && sortedReload.Items[0].CompletedAtTicks > sortedReload.Items[1].CompletedAtTicks,
        "completion order survives reloading");
    sortedReload.Toggle(0);
    Check(!sortedReload.Items[0].Completed && sortedReload.Items[0].CompletedAtTicks == 0 && sortedReload.VisibleIndices.First()==0,
        "restoring a todo clears completion time and returns it to the unfinished group");
    sortedReload.Toggle(0);
    Check(sortedReload.VisibleIndices.First()==0, "completing a restored todo uses a fresh completion order");
    Check(!restored.Toggle(-1) && !restored.Toggle(999), "invalid checkbox indices do not modify records");
    restored.Draft.Replace(new string('中', TodoList.MaxTextLength + 1));
    Check(!restored.Add() && restored.Draft.Text.Length > 0 && restored.Items.Count == 2, "overlong todo retains the draft and reports a limit");
    Check(!Directory.EnumerateFiles(todoRoot, "*.tmp").Any(), "todo saves leave no temporary files");
    var assistantTodos = new TodoList(todoRoot, "assistant-save");
    assistantTodos.Draft.Replace("手动编辑中的草稿");
    using (var created = JsonDocument.Parse(assistantTodos.ExecuteTool("add_todo", "{\"text\":\"准备食物后下矿\"}")))
        Check(created.RootElement.GetProperty("added").GetBoolean() && assistantTodos.Items.Count==1, "assistant add tool creates a persisted todo");
    Check(assistantTodos.Draft.Text=="手动编辑中的草稿", "assistant additions preserve the manual todo draft");
    assistantTodos.ExecuteTool("add_todo", "{\"text\":\"准备食物后下矿\"}");
    Check(assistantTodos.Items.Count==1, "repeated assistant additions do not duplicate an unfinished todo");
    assistantTodos.Toggle(0);
    using (var readTodos = JsonDocument.Parse(assistantTodos.ExecuteTool("get_todos", "{}")))
        Check(readTodos.RootElement.GetProperty("items")[0].GetProperty("Completed").GetBoolean(), "assistant can read the current completion state");
    assistantTodos.ExecuteTool("add_todo", "{\"text\":\"准备食物后下矿\"}");
    Check(assistantTodos.Items.Count==2 && !assistantTodos.Items[1].Completed, "a completed task can be added as a new unfinished todo");
    Check(assistantTodos.ExecuteTool("add_todo", "{\"text\":\"x\",\"path\":\"other-folder\"}").Contains("error"), "todo tools reject unexpected file or command parameters");
    Check(GameTools.TryArguments("add_todo", JsonSerializer.Serialize(new { text=new string('中',300) }), out _)
        && !GameTools.TryArguments("add_todo", JsonSerializer.Serialize(new {text=new string('中',301)}), out _), "assistant todo arguments accept escaped Chinese within the text limit only");
    for(int i=0;i<32;i++) { assistantTodos.Draft.Replace("任务 "+i); assistantTodos.Add(); }
    using (var bounded = JsonDocument.Parse(assistantTodos.ExecuteTool("get_todos", "{}")))
        Check(bounded.RootElement.GetProperty("items").GetArrayLength()==30 && bounded.RootElement.GetProperty("truncated").GetBoolean(), "todo read results explicitly mark a truncated list");
    using (var searched = JsonDocument.Parse(assistantTodos.ExecuteTool("get_todos", "{\"query\":\"下矿\"}")))
        Check(searched.RootElement.GetProperty("items").GetArrayLength()==2, "todo search can retrieve older tasks outside the visible thirty");
    var protocolTodos = new TodoList(todoRoot, "protocol-save");
    protocolTodos.Draft.Replace("给作物浇水"); protocolTodos.Add(); protocolTodos.Toggle(0);
    var toolDispatch = new MainThreadQueries((name,args)=>name=="get_overview" ? "{\"worldReady\":true}" : protocolTodos.ExecuteTool(name,args));
    int todoRound = 0;
    using(var todoHttp = new HttpClient(new StubHandler(async (request, token)=> {
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var wire=body.RootElement.GetProperty("messages");
        if(++todoRound==1) return StreamAnswer(ToolChunk(new object[]{new {index=0,id="todo_read",type="function",function=new {name="get_todos",arguments="{}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
        if(todoRound==2) {
            string result=wire[wire.GetArrayLength()-1].GetProperty("content").GetString()!;
            Check(result.Contains("给作物浇水") && result.Contains("true"), "model receives saved todo content and completion state through the tool protocol");
            return StreamAnswer(ToolChunk(new object[]{new {index=0,id="todo_add",type="function",function=new {name="add_todo",arguments="{\"text\":\"带食物去煤矿森林\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
        }
        Check(wire[wire.GetArrayLength()-1].GetProperty("content").GetString()!.Contains("\"added\":true"), "model receives confirmed successful todo creation before claiming success");
        return StreamAnswer(Event("已加入待办：带食物去煤矿森林。")+End());
    }))) {
        var task=new DeepSeekClient(new FakeSecret(dummyKey),todoHttp).AskAsync(options,new Conversation().BuildMessages("查看待办，并帮我记下带食物去煤矿森林"),CancellationToken.None,_=>{},toolDispatch);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted && timer.ElapsedMilliseconds<5000) {toolDispatch.Tick();Thread.Sleep(1);}
        Check(task.IsCompletedSuccessfully && task.Result.Contains("已加入待办") && protocolTodos.Items.Count==2,
            "streamed conversation can read then add todos through the main-thread dispatcher");
    }
    using(var canceled = new CancellationTokenSource()) {
        var task=toolDispatch.QueryAsync("add_todo","{\"text\":\"不应保存\"}",canceled.Token);
        canceled.Cancel(); toolDispatch.Tick();
        Check(task.IsCanceled && protocolTodos.Items.Count==2,"canceled queued additions cannot create todos after a save switch");
    }
    string assistantFile=Directory.EnumerateFiles(todoRoot,"*.json").Single(f=>File.ReadAllText(f).Contains("CompletedAtTicks") && f==new ConversationStore(todoRoot).PathFor("assistant-save"));
    Check(new FileInfo(assistantFile).Length>0,"assistant todo records remain in the supplied mod data directory");
    string file = new ConversationStore(todoRoot).PathFor("save-A");
    File.WriteAllText(file, "damaged original");
    var damaged = new TodoList(todoRoot, "save-A"); damaged.Draft.Replace("do not overwrite");
    Check(damaged.Error.Length > 0 && !damaged.Add() && File.ReadAllText(file) == "damaged original", "a damaged todo file is preserved and blocks overwrites");
    File.WriteAllText(file, JsonSerializer.Serialize(Enumerable.Range(0, TodoList.MaxItems).Select(i => new TodoItem("task " + i))));
    var full = new TodoList(todoRoot, "save-A"); full.Draft.Replace("extra");
    Check(!full.Add() && full.Items.Count == TodoList.MaxItems, "full todo lists retain all existing entries");
    string blockedTodoPath = Path.Combine(todoRoot, "not-a-directory"); File.WriteAllText(blockedTodoPath, "keep");
    var unwritable = new TodoList(blockedTodoPath, "save"); unwritable.Draft.Replace("保留草稿");
    Check(!unwritable.Add() && unwritable.Items.Count == 0 && unwritable.Draft.Text == "保留草稿" && unwritable.Error.Length > 0, "failed saving preserves the draft without pretending a todo was added");
}
finally { Directory.Delete(todoRoot, true); }

Check(ChatCommands.Parse("普通问题").Kind==ChatCommandKind.None && ChatCommands.Parse(" /clearchat ").Kind==ChatCommandKind.ClearChat,
    "slash commands are recognized only at the beginning of the input");
Check(ChatCommands.Parse("/clearchat extra").Kind==ChatCommandKind.Invalid && ChatCommands.Parse("/cleartodo extra").Kind==ChatCommandKind.Invalid,
    "destructive commands reject unexpected arguments");
Check(ChatCommands.Parse("/help + “能标记地图吗？”").Argument=="能标记地图吗？" && ChatCommands.Parse("/help \"功能有哪些？\"").Argument=="功能有哪些？",
    "help supports plain questions and quoted questions with an optional plus");
Check(!GameTools.TryArguments("offer_map_marker","{}",out _) && GameTools.Definitions.Length==10,"removed map tools cannot be invoked");
string commandRoot=Path.Combine(Path.GetTempPath(),"stardew-command-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(commandRoot);
try {
    var commandStore=new ConversationStore(Path.Combine(commandRoot,"conversations"));
    var commandMemory=new Conversation();commandMemory.AddExchange("只属于A的旧记录","秘密摘要源");commandMemory.ApplySummary("A的旧摘要",2);
    await commandStore.SaveAsync("A",commandMemory.Snapshot());
    commandMemory.AddExchange("最近问题","最近回答");await commandStore.SaveAsync("A",commandMemory.Snapshot());
    await commandStore.SaveAsync("B",commandMemory.Snapshot());
    string orphan=commandStore.PathFor("A")+".old.tmp"; await File.WriteAllTextAsync(orphan,"A的崩溃残留");
    var commandTodos=new TodoList(Path.Combine(commandRoot,"todos"),"A");
    for(int i=0;i<35;i++) {commandTodos.Draft.Replace("待办 "+i);commandTodos.Add();}
    commandTodos.Toggle(0);commandTodos.Draft.Replace("未提交草稿");
    var otherTodos=new TodoList(Path.Combine(commandRoot,"todos"),"B"); otherTodos.Draft.Replace("B的任务");otherTodos.Add();
    int networkCalls=0;
    using var noNetwork=new HttpClient(new StubHandler((_,_)=>{networkCalls++;throw new Exception("local command must not use HTTP");}));
    var commandSession=new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey),noNetwork),store:commandStore,todoList:()=>commandTodos);
    commandSession.SwitchSave("A");await Finish(commandSession);
    int resetKeyRequests=0;commandSession.KeyConfigurationRequested+=()=>resetKeyRequests++;
    commandSession.Draft.Replace("/resetkey");
    Check(commandSession.Send(options) && resetKeyRequests==1 && commandSession.Draft.Text=="" && networkCalls==0,
        "resetkey opens local configuration without networking or retaining the command in the draft");
    Check((await commandStore.LoadAsync("A")).State.Transcript.Length>0 && commandTodos.Items.Count==35,
        "resetkey preserves current-save chat history and todos");
    Check(ChatCommands.Parse("/resetkey extra").Kind==ChatCommandKind.Invalid,
        "resetkey rejects unexpected arguments");

    commandSession.Draft.Replace("/clearchat");Check(commandSession.Send(options),"clearchat dispatches locally");await Finish(commandSession);
    var empty=(await commandStore.LoadAsync("A")).State;
    Check(empty.Transcript.Length==0 && empty.Recent.Length==0 && empty.Summary=="" && empty.Compressions==0,
        "clearchat removes complete transcript, recent context and summary");
    Check(!File.Exists(commandStore.PathFor("A")+".bak") && !File.Exists(orphan),"clearchat removes the current save backup and stale temporary history");
    Check((await commandStore.LoadAsync("B")).State.Transcript.Length==4 && commandTodos.Items.Count==35,"clearchat preserves other saves and todos");
    var reopenedCommand=new ChatSession(store:new ConversationStore(Path.Combine(commandRoot,"conversations")));
    reopenedCommand.SwitchSave("A");await Finish(reopenedCommand);
    Check(reopenedCommand.Display.Count==0,"cleared history cannot return after restarting the session");
    Check(commandSession.Display.Count==1 && commandSession.Display[0].Text.Contains("已清除"),"clear confirmation is a local notice rather than saved conversation history");
    commandSession.Draft.Replace("/cleartodo");Check(commandSession.Send(options),"cleartodo dispatches locally");
    Check(commandTodos.Items.Count==0 && commandTodos.Draft.Text=="" && new TodoList(Path.Combine(commandRoot,"todos"),"A").Items.Count==0,
        "cleartodo clears completed, unfinished and hidden old entries and persists the empty list");
    Check(new TodoList(Path.Combine(commandRoot,"todos"),"B").Items.Count==1,"cleartodo preserves other saves");
    commandSession.Draft.Replace("/clearchat accidental words");commandSession.Send(options);
    Check(commandSession.Display[^1].Text.Contains("参数不正确"),"invalid clear syntax produces usage without executing");
    commandSession.Draft.Replace("/help");commandSession.Send(options);
    Check(commandSession.Display[^1].Text.Contains("/cleartodo") && networkCalls==0,"bare help and clear commands need no API call");
    var legacy=new Conversation();legacy.AddExchange("旧地图问题","旧回答");
    string legacyJson=JsonSerializer.Serialize(legacy.Snapshot()).Replace("\"Role\":\"assistant\"","\"Role\":\"assistant\",\"MapTargetId\":\"retired_target\"");
    await File.WriteAllTextAsync(commandStore.PathFor("legacy"),legacyJson);
    Check((await commandStore.LoadAsync("legacy")).State.Transcript.Length==2,"older conversation files remain readable after map metadata removal");
    var helpQueries=new FakeGameQueries();int helpRequests=0;
    using var helpHttp=new HttpClient(new StubHandler(async (request,token)=> {
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=body.RootElement;
        Check(!root.TryGetProperty("tools",out _) && root.GetProperty("tool_choice").GetString()=="none","help disables all game queries and todo writes");
        var helpMessages=root.GetProperty("messages");
        Check(helpMessages.GetArrayLength()==2 && helpMessages[0].GetProperty("content").GetString()!.Contains(ModHelp.Facts)
            && helpMessages[1].GetProperty("content").GetString()=="本Mod能做什么？","help uses current authoritative capabilities and the question only");
        helpRequests++;return StreamAnswer(Event("可以对话、查询游戏状态和管理待办；不支持地图标记。")+End());
    }));
    var helpSession=new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey),helpHttp),helpQueries,commandStore,()=>commandTodos);
    helpSession.SwitchSave("help-save");await Finish(helpSession);
    helpSession.Draft.Replace("/help + “本Mod能做什么？”");Check(helpSession.Send(options),"help question is sent asynchronously");await Finish(helpSession);
    Check(helpRequests==1 && helpQueries.Calls.Count==0 && helpSession.Display[^1].Text.Contains("不支持地图标记"),"help returns a normal streamed answer without touching game state");
    Check((await commandStore.LoadAsync("help-save")).State.Transcript[0].Text.StartsWith("/help"),"help questions use the existing per-save conversation storage");
    using var delayedHttp=new HttpClient(new StubHandler(async(_,token)=>{await Task.Delay(300,token);return StreamAnswer(Event("迟到回答")+End());}));
    var interrupted=new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey),delayedHttp),store:commandStore,todoList:()=>commandTodos);
    interrupted.SwitchSave("busy");await Finish(interrupted);interrupted.Draft.Replace("尚未完成的问题");interrupted.Send(options);
    interrupted.Draft.Replace("/clearchat");Check(interrupted.Send(options),"clearchat can interrupt an in-flight answer");await Finish(interrupted);await Task.Delay(350);interrupted.Tick();
    Check((await commandStore.LoadAsync("busy")).State.Transcript.Length==0 && !interrupted.Display.Any(m=>m.Role=="assistant"),"late answers cannot repopulate a cleared conversation");
}
finally {Directory.Delete(commandRoot,true);}


int ragRound=0,ragLookups=0;
using(var ragHttp=new HttpClient(new StubHandler(async(request,token)=>{
    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var req=body.RootElement;
    Check(req.GetProperty("tool_choice").ValueKind==JsonValueKind.Object
        ? req.GetProperty("thinking").GetProperty("type").GetString()=="disabled" && !req.TryGetProperty("reasoning_effort",out _)
        : req.GetProperty("thinking").GetProperty("type").GetString()=="enabled"&&req.GetProperty("reasoning_effort").GetString()=="high",
        "forced retrieval disables thinking; evidence synthesis enables high reasoning without incompatible tool_choice");
    if(++ragRound==1)
    {
        Check(req.GetProperty("tool_choice").GetProperty("function").GetProperty("name").GetString()=="wiki_search","mechanics planning forces evidence retrieval before answering");
        string thought="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"先查证资料。\"},\"finish_reason\":null}]}\n\n";
        return StreamAnswer(ToolChunk(new object[]{new{index=0,id="wiki1",type="function",function=new{name="wiki_search",arguments="{\"query\":\"Strawberry\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
    }
    Check(req.GetProperty("messages").EnumerateArray().Any(m=>m.TryGetProperty("reasoning_content",out var r)&&r.GetString()==""),"non-thinking retrieval passes an explicit reasoning field when resuming thinking");
    return StreamAnswer(Event("结合资料规划种植。")+End());
})))
{
    var ragApi=new DeepSeekClient(new FakeSecret(dummyKey),ragHttp,(query,token)=>{ragLookups++;return Task.FromResult("{\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Strawberry\",\"stale\":false,\"body\":\"fixture\"}]}");});
    var text=new StringBuilder();string answer=await ragApi.AskAsync(options,new[]{new ChatMessage("system","fixture"),new ChatMessage("user","帮我规划草莓收益")},default,t=>text.Append(t),new FakeGameQueries());
    Check(ragLookups==1&&answer.Contains("https://stardewvalleywiki.com/Strawberry")&&!answer.Contains("先查证资料"),"final streaming response cites retrieved URL without exposing internal reasoning");
}

using(var lowHttp=new HttpClient(new StubHandler(async(req,token)=>{
    using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
    Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="enabled"&&body.RootElement.GetProperty("reasoning_effort").GetString()=="low","ordinary game questions use light reasoning while planning remains high");
    return StreamAnswer(Event("请根据实际游戏数据确认。")+End());
})))
{
    var light=new DeepSeekClient(new FakeSecret(dummyKey),lowHttp,(_,_)=>Task.FromResult("{\"quality\":\"Low\",\"chunks\":[]}"));
    await light.AskAsync(options,new[]{new ChatMessage("system","fixture"),new ChatMessage("user","我的背包有什么")},default,_=>{},new FakeGameQueries());
}

var refs=CitationMarkup.Parse("铁矿在矿井[https://stardewvalleywiki.com/Iron_Ore]，铜矿另查[https://stardewvalleywiki.com/Copper_Ore https://stardewvalleywiki.com/Iron_Ore]。");
Check(string.Concat(refs.Select(p=>p.Text))=="铁矿在矿井[1]，铜矿另查[2][1]。","inline source groups use stable per-answer numbers and preserve surrounding text");
Check(refs.Where(p=>p.Url!=null).Select(p=>p.Url).Distinct().Count()==2,"repeated citations retain a URL hover target without creating another number");
Check(CitationMarkup.Parse("旧回答。\n来源：https://stardewvalleywiki.com/Iron_Ore").Select(p=>p.Text).Aggregate("",(a,b)=>a+b)=="旧回答。[1]","legacy source lines render inline without rewriting saved history");
Check(CitationMarkup.Parse("网页[链接](https://smapi.io/)正文").Any(p=>p.Url=="https://smapi.io/"&&p.Text=="[1]"),"old Markdown links can render as inline citation numbers");
Check(CitationMarkup.Parse("这是[1]和[一个说明]，不是来源").All(p=>p.Url==null),"plain brackets and numbers remain ordinary text");
Check(CitationMarkup.Parse("[https://localhost/a]").All(p=>p.Url==null),"unsafe local URLs are not converted into citations");
Check(CitationMarkup.Parse("[https://stardewvalleywiki.com/Milk_(Large)]").Single(p=>p.Url!=null).Url!.Contains("Milk_(Large)"),"balanced URL parentheses are preserved");

Check(CitationMarkup.Parse("自己制作[http://stardewvalleywiki.com/Cave_Jelly]。").Any(p=>p.Text=="[1]"&&p.Url=="https://stardewvalleywiki.com/Cave_Jelly"),"legacy http Wiki markers normalize to HTTPS and render numbered citations");
int cachedRound=0;
using(var cachedHttp=new HttpClient(new StubHandler(async(req,token)=>{
    using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
    if(++cachedRound==1)return StreamAnswer(ToolChunk(new object[]{new{index=0,id="cached1",type="function",function=new{name="wiki_search",arguments="{\"query\":\"Fish Smoker\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
    Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","simple all-cache answer skips extra reasoning after retrieval");
    return StreamAnswer(Event("查到的配方说明[http://stardewvalleywiki.com/Fish_Smoker]。")+End());
})))
{
    var cachedApi=new DeepSeekClient(new FakeSecret(dummyKey),cachedHttp,(_,_)=>Task.FromResult("{\"cacheOnly\":true,\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Fish_Smoker\",\"stale\":false}]}"));
    string answer=await cachedApi.AskAsync(options,new[]{new ChatMessage("user","熏鱼机怎么获得")},default,_=>{},new FakeGameQueries());
    Check(answer=="查到的配方说明。","all-cache answers strip both model source markers and fallback sources");
}

int preflightRequests=0;
using(var preflightHttp=new HttpClient(new StubHandler(async(req,token)=>{
    preflightRequests++;using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
    Check(body.RootElement.GetProperty("tool_choice").GetString()=="auto"&&body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","preflight cache hit avoids forced planning and extra reasoning");
    return StreamAnswer(Event("缓存资料回答[https://stardewvalleywiki.com/Fish_Smoker]。")+End());
})))
{
    var cacheGame=new FakeGameQueries();
    var preflight=new DeepSeekClient(new FakeSecret(dummyKey),preflightHttp,(_,_)=>throw new Exception("should not research again"),cachedKnowledge:(_,_)=>Task.FromResult<string?>("{\"cacheOnly\":true,\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Fish_Smoker\",\"stale\":false}]}"));
    string answer=await preflight.AskAsync(options,new[]{new ChatMessage("user","熏鱼机怎么获得")},default,_=>{},cacheGame);
    Check(preflightRequests==1&&answer=="缓存资料回答。","pure cache hit needs one model request and returns no citation markup");
    Check(cacheGame.Calls.Count==2,"cache fast path still rereads current overview and todos");
}

Check(GameTools.TryArguments("wiki_search","{\"query\":\"summer fish\",\"refresh\":true}",out var refreshQuery)&&refreshQuery=="summer fish"&&GameTools.RefreshRequested("{\"refresh\":true}"),"knowledge tool accepts explicit boolean refresh authorization");
Check(!GameTools.TryArguments("wiki_search","{\"query\":\"summer fish\",\"refresh\":\"true\"}",out _),"refresh rejects malformed non-boolean arguments");
int refreshRound=0,freshRequests=0;
using(var refreshHttp=new HttpClient(new StubHandler(async(req,token)=>{
    using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
    if(++refreshRound==1)return StreamAnswer(ToolChunk(new object[]{new{index=0,id="fresh1",type="function",function=new{name="wiki_search",arguments="{\"query\":\"summer fish\",\"refresh\":true}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
    return StreamAnswer(Event("取得了新的鱼类资料。")+End());
})))
{
    var refreshApi=new DeepSeekClient(new FakeSecret(dummyKey),refreshHttp,(_,_)=>throw new Exception("must not be forced back to cached retriever"),
        cachedKnowledge:(_,_)=>Task.FromResult<string?>("{\"cacheOnly\":true,\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Summer\"}]}"),
        freshKnowledge:(_,_)=>{freshRequests++;return Task.FromResult("{\"cacheOnly\":false,\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Summer\",\"stale\":false}]}");});
    string answer=await refreshApi.AskAsync(options,new[]{new ChatMessage("user","夏季有什么鱼可以钓")},default,_=>{},new FakeGameQueries());
    Check(freshRequests==1&&answer.Contains("https://stardewvalleywiki.com/Summer"),"model rejecting cached evidence invokes fresh provider and restores online citations");
}

int autoRefreshTurns=0,autoFreshCalls=0;
using(var retryHttp=new HttpClient(new StubHandler((req,token)=>Task.FromResult(++autoRefreshTurns==1
    ?StreamAnswer(ToolChunk(new object[]{new{index=0,id="auto1",type="function",function=new{name="wiki_search",arguments="{\"query\":\"summer fish\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n")
    :StreamAnswer(Event("新资料回答。")+End())))))
{
    var automatic=new DeepSeekClient(new FakeSecret(dummyKey),retryHttp,(_,_)=>throw new Exception("cache must not trap model"),
        cachedKnowledge:(_,_)=>Task.FromResult<string?>("{\"cacheOnly\":true,\"quality\":\"High\",\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Summer\"}]}"),
        freshKnowledge:(_,_)=>{autoFreshCalls++;return Task.FromResult("{\"cacheOnly\":false,\"quality\":\"Low\",\"chunks\":[]}");});
    await automatic.AskAsync(options,new[]{new ChatMessage("user","夏季有什么鱼可以钓")},default,_=>{},new FakeGameQueries());
    Check(autoFreshCalls==1,"model searching again after cached evidence automatically bypasses the cache even without refresh flag");
}
Check(ChatCommands.Parse("/search").Kind==ChatCommandKind.Search && ChatCommands.Parse("/search 夏季能钓什么鱼？").Argument=="夏季能钓什么鱼？",
    "search command recognizes its question and empty usage form");
Check(GameTools.TodoWriteRequested("帮我记下明天找罗宾") && GameTools.TodoWriteRequested("添加待办：浇水")
    && !GameTools.TodoWriteRequested("我明天应该做什么？") && !GameTools.TodoWriteRequested("还有哪些待办没完成？")
    && !GameTools.TodoWriteRequested("不要擅自添加待办"),"todo write permission requires a current explicit request rather than advice or a read request");
var deniedTodoGame=new FakeGameQueries();int deniedTodoRound=0;
using(var deniedTodoHttp=new HttpClient(new StubHandler(async (request,token)=>{
    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    Check(!body.RootElement.GetProperty("tools").EnumerateArray().Any(t=>t.GetProperty("function").GetProperty("name").GetString()=="add_todo"),
        "ordinary advice does not advertise the todo write tool");
    if(++deniedTodoRound==1)return StreamAnswer(ToolChunk(new object[]{new{index=0,id="unrequested",type="function",function=new{name="add_todo",arguments="{\"text\":\"自行添加的建议\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
    Check(body.RootElement.GetProperty("messages").EnumerateArray().Any(m=>m.GetProperty("content").ValueKind==JsonValueKind.String&&m.GetProperty("content").GetString()!.Contains("Todo write denied")),"unrequested todo calls are denied in execution as well as hidden in definitions");
    return StreamAnswer(Event("建议明天浇水。")+End());
})))
{
    await new DeepSeekClient(new FakeSecret(dummyKey),deniedTodoHttp).AskAsync(options,new Conversation().BuildMessages("明天应该做什么？"),default,_=>{},deniedTodoGame);
    Check(!deniedTodoGame.Calls.Any(c=>c.StartsWith("add_todo")),"unrequested todo writes never reach the save's todo list");
}
int researchRound=0,researchSearches=0,ordinaryProviders=0;
var researchGame=new FakeGameQueries();
var researchHistory=new Conversation();researchHistory.AddExchange("历史假设：我有99999金币","历史NPC位置是假地点");
researchHistory.ApplySummary("旧摘要不能作为当前状态",2);
string liveEvidence="{\"quality\":\"High\",\"cacheOnly\":false,\"chunks\":[{\"url\":\"https://stardewvalleywiki.com/Summer\",\"stale\":false,\"body\":\"Fixture live fish conditions.\"}]}";
using(var researchHttp=new HttpClient(new StubHandler(async (request,token)=>{
    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var json=body.RootElement;
    int round=++researchRound;
    if(round<=2)Check(json.GetProperty("thinking").GetProperty("type").GetString()=="enabled" && json.GetProperty("reasoning_effort").GetString()=="high"
        && !json.TryGetProperty("tool_choice",out _),"research discovery uses high thinking with automatic tools");
    else Check(json.GetProperty("thinking").GetProperty("type").GetString()=="disabled"&&!json.TryGetProperty("tools",out _)&&json.GetProperty("tool_choice").GetString()=="none",
        "research budget forces direct synthesis without more thinking or tools");
    Check(!json.ToString().Contains("99999")&&!json.ToString().Contains("旧摘要")&&!json.ToString().Contains("假地点"),"search cannot use historical conversation or summary as game observations");
    if(round<=2)
    {
        var tools=new List<object>();
        if(round==1)
        {
            for(int i=0;i<9;i++)tools.Add(new{index=tools.Count,id="inventory"+i,type="function",function=new{name="get_inventory",arguments=JsonSerializer.Serialize(new{query="item"+i})}});
            tools.Add(new{index=tools.Count,id="overviewAgain",type="function",function=new{name="get_overview",arguments="{}"}});
        }
        for(int i=0;i<2;i++)tools.Add(new{index=tools.Count,id="research"+round+"_"+i,type="function",function=new{name="wiki_search",arguments=JsonSerializer.Serialize(new{query="summer fish "+round+" "+i})}});
        return StreamAnswer(ToolChunk(tools.ToArray())+Event("","tool_calls")+"data: [DONE]\n\n");
    }
    return StreamAnswer(Event("已核实的夏季信息[https://stardewvalleywiki.com/Summer]。不保留编造来源[https://example.com/invented]。")+End());
})))
{
    var researchClient=new DeepSeekClient(new FakeSecret(dummyKey),researchHttp,
        knowledge:(_,_)=>{ordinaryProviders++;throw new Exception("ordinary path forbidden");},
        cachedKnowledge:(_,_)=>{ordinaryProviders++;throw new Exception("cache path forbidden");},
        freshKnowledge:(_,_)=>{ordinaryProviders++;throw new Exception("refresh path still writes cache");},
        uncachedKnowledge:(q,_)=>{researchSearches++;return Task.FromResult(liveEvidence.Replace("Fixture live fish conditions.",q));});
    string result=await researchClient.AskAsync(options,researchHistory.BuildMessages("夏季能钓什么鱼？"),default,_=>{},researchGame,research:true);
    Check(researchRound==3&&researchSearches==4&&ordinaryProviders==0,"research stops after four unique searches and performs one final answer request");
    Check(researchGame.Calls.Count(c=>c.StartsWith("get_inventory"))==9&&researchGame.Calls.Count(c=>c.StartsWith("get_overview"))==2,
        "research permits necessary detail batches and repeated fresh observations within its game budget");
    Check(result.Contains("[https://stardewvalleywiki.com/Summer]")&&!result.Contains("example.com")&&CitationMarkup.Parse(result).Any(p=>p.Text=="[1]"),
        "search keeps only retrieved source URLs using the existing inline citation renderer");
}
using(var cancelResearch=new CancellationTokenSource())
using(var cancelSearchHttp=new HttpClient(new StubHandler(async (_,token)=>{cancelResearch.Cancel();await Task.Delay(Timeout.Infinite,token);return Answer();})))
{
    var error=await Failure(()=>new DeepSeekClient(new FakeSecret(dummyKey),cancelSearchHttp,uncachedKnowledge:(_,_)=>Task.FromResult(liveEvidence))
        .AskAsync(options,Conversation.BuildSearchMessages("查鱼"),cancelResearch.Token,_=>{},new FakeGameQueries(),research:true));
    Check(error is OperationCanceledException,"bounded research remains immediately cancellable");
}
string searchRoot=Path.Combine(Path.GetTempPath(),"copilot-search-session-"+Guid.NewGuid().ToString("N"));
try
{
    var searchStore=new ConversationStore(searchRoot);var oldSearchHistory=new Conversation();oldSearchHistory.AddExchange("旧状态99999金币","旧状态900体力");
    await searchStore.SaveAsync("search-save",oldSearchHistory.Snapshot());int searchSessionCalls=0;
    using var searchSessionHttp=new HttpClient(new StubHandler(async(request,token)=>{
        using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Check(!json.RootElement.GetRawText().Contains("99999")&&!json.RootElement.GetRawText().Contains("900体力"),"search session sends only its current question and observations");
        if(++searchSessionCalls==1)return StreamAnswer(ToolChunk(new object[]{new{index=0,id="sessionSearch",type="function",function=new{name="wiki_search",arguments="{\"query\":\"summer fish\"}"}}})+Event("","tool_calls")+"data: [DONE]\n\n");
        return StreamAnswer(Event("夏季鱼类资料[https://stardewvalleywiki.com/Summer]。")+End());
    }));
    var searchSession=new ChatSession(new DeepSeekClient(new FakeSecret(dummyKey),searchSessionHttp,uncachedKnowledge:(_,_)=>Task.FromResult(liveEvidence)),new FakeGameQueries(),searchStore);
    searchSession.SwitchSave("search-save");await Finish(searchSession);
    searchSession.Draft.Replace("/search");Check(!searchSession.Send(options)&&searchSessionCalls==0,"empty search shows local usage without contacting the model");
    searchSession.Draft.Replace("/search 夏季能钓什么鱼？");Check(searchSession.Send(options),"search dispatches from the chat input");await Finish(searchSession);
    var saved=(await searchStore.LoadAsync("search-save")).State;
    Check(saved.Transcript.Length==4&&saved.Transcript[0].Text=="旧状态99999金币"&&saved.Transcript[2].Text.StartsWith("/search "),
        "search stores its successful exchange without deleting or rewriting old conversation history");
}
finally{Directory.Delete(searchRoot,true);}

var completions=new CommandCompletion();
completions.Update("",0,true);Check(!completions.Visible,"empty editor does not show commands before a slash");
completions.Update("/",1,true);Check(completions.Visible&&completions.Selected==0&&CommandCompletion.Entries.Count==6,
    "typing a slash into an empty editor shows every available special command");
completions.Move(-1);Check(completions.Selected==5,"up arrow wraps to the last command");
completions.Move(1);Check(completions.Selected==0&&completions.Complete()=="/search ","down arrow and completion select search with a trailing question space");
Check(!completions.Visible&&completions.Complete()==null,"command completion hides its menu without executing or completing again");
completions.Update("/search ",8,true);completions.Update("/",1,true);
Check(completions.Complete(3)=="/clearchat","destructive command selection only returns its text for the editor");
completions.Update("",0,true);completions.Update("/",1,true);completions.Dismiss();completions.Update("/",1,true);
Check(!completions.Visible,"Escape dismisses suggestions until the editor changes");
completions.Update("/",1,false);Check(!completions.Visible,"commands stay hidden in todo/help tabs or during IME composition");
completions.Update("/",1,true);Check(completions.Visible,"returning to an eligible slash editor restores command suggestions");
completions.Update("普通句子/",6,true);Check(!completions.Visible,"a slash inside normal text does not open command suggestions");
Check(CommandCompletion.Entries.All(e=>ChatCommands.Parse(e.Name).Kind!=ChatCommandKind.Invalid),"all displayed completions correspond to implemented commands");

string largeResearchText=new string('夏',30000)+"[https://stardewvalleywiki.com/Summer]";
string largeResearchReasoning=new string('思',210000);
foreach(string transport in new[]{"sse-known","sse-unknown","json-known","json-unknown"})
{
    bool streaming=transport.StartsWith("sse"),unknown=transport.EndsWith("unknown");int sizeRound=0;
    using var sizeHttp=new HttpClient(new StubHandler(async(request,token)=>{
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Check(body.RootElement.GetProperty("max_tokens").GetInt32()==DeepSeekClient.MaxSearchTokens,"search raises the generation budget independently of ordinary chat: "+transport);
        string payload;
        if(++sizeRound==1)
        {
            object[] tool=new object[]{new{index=0,id="sizeEvidence",type="function",function=new{name="wiki_search",arguments="{\"query\":\"summer\"}"}}};
            payload=streaming?ToolChunk(tool)+Event("","tool_calls")+"data: [DONE]\n\n"
                :JsonSerializer.Serialize(new{choices=new[]{new{message=new{content="",tool_calls=tool},finish_reason="tool_calls"}}});
        }
        else if(streaming)
        {
            var data=new StringBuilder();
            for(int i=0;i<largeResearchReasoning.Length;i+=2000)data.Append("data: ").Append(JsonSerializer.Serialize(new{
                choices=new[]{new{delta=new{reasoning_content=largeResearchReasoning.Substring(i,2000)},finish_reason=(string?)null}}
            })).Append("\n\n");
            data.Append(Event(largeResearchText)).Append(End());payload=data.ToString();
            Check(Encoding.UTF8.GetByteCount(payload)>DeepSeekClient.MaxResponseBytes,"long research fixture exceeds the old wire limit: "+transport);
        }
        else payload=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=largeResearchText,reasoning_content=largeResearchReasoning},finish_reason="stop"}}});
        return new HttpResponseMessage(HttpStatusCode.OK){Content=unknown?new ChunkedContent(Encoding.UTF8.GetBytes(payload)):new StringContent(payload,Encoding.UTF8,streaming?"text/event-stream":"application/json")};
    }));
    var collectedResearch=new StringBuilder();
    string result=await new DeepSeekClient(new FakeSecret(dummyKey),sizeHttp,uncachedKnowledge:(_,_)=>Task.FromResult(liveEvidence))
        .AskAsync(options,Conversation.BuildSearchMessages("安排夏季"),default,streaming?t=>collectedResearch.Append(t):null,new FakeGameQueries(),research:true);
    Check(result==largeResearchText&&(!streaming||collectedResearch.ToString()==result),"search accepts a long response with large private reasoning without exposing it: "+transport);
}
string sizeStoreRoot=Path.Combine(Path.GetTempPath(),"copilot-long-search-"+Guid.NewGuid().ToString("N"));
try
{
    var longSearchHistory=new Conversation();longSearchHistory.AddExchange("/search 规划夏季",largeResearchText);
    var sizeStore=new ConversationStore(sizeStoreRoot);await sizeStore.SaveAsync("long-search",longSearchHistory.Snapshot());
    var loaded=(await new ConversationStore(sizeStoreRoot).LoadAsync("long-search")).State;
    Check(loaded.Transcript[1].Text==largeResearchText&&loaded.Recent[1].Text==largeResearchText,"long search answers persist and restore without invalid-history failures");
    var normalTooLong=new Conversation();normalTooLong.AddExchange("普通规划问题",largeResearchText);
    Check(await Failure(()=>sizeStore.SaveAsync("normal-too-long",normalTooLong.Snapshot())) is InvalidDataException,"ordinary saved answers retain the original character limit");
}
finally{Directory.Delete(sizeStoreRoot,true);}
foreach(bool unknown in new[]{false,true})
{
    using var overSizeHttp=new HttpClient(new StubHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){
        Content=unknown?new ChunkedContent(new byte[DeepSeekClient.MaxSearchResponseBytes+1]):new ByteArrayContent(new byte[DeepSeekClient.MaxSearchResponseBytes+1])
    })));
    var error=await Failure(()=>new DeepSeekClient(new FakeSecret(dummyKey),overSizeHttp,uncachedKnowledge:(_,_)=>Task.FromResult(liveEvidence))
        .AskAsync(options,Conversation.BuildSearchMessages("研究夏季"),default,null,new FakeGameQueries(),research:true));
    Check(error.Message.Contains("返回内容过大"),"research still enforces its larger transport boundary, unknown-length="+unknown);
}

double budgetClock=0;var timeBudget=new ResearchBudget(()=>budgetClock);
Check(!timeBudget.ShouldFinalize(0)&&timeBudget.LookupRemaining==25,"research reserves time for synthesis rather than consuming the whole minute in retrieval");
budgetClock=25;Check(timeBudget.ShouldFinalize(0)&&timeBudget.StopReason=="lookup_deadline","research stops discovery at its stage deadline");
var duplicateBudget=new ResearchBudget(()=>0);Check(duplicateBudget.TrySearch("Summer fish")&&!duplicateBudget.TrySearch("summer  fish!"),"repeated normalized searches do not call the network again");
var noveltyBudget=new ResearchBudget(()=>0);
using(var e=JsonDocument.Parse(liveEvidence)){noveltyBudget.ObserveEvidence(e.RootElement);noveltyBudget.ObserveEvidence(e.RootElement);Check(!noveltyBudget.ShouldFinalize(0),"one unchanged retrieval still permits a necessary follow-up");noveltyBudget.ObserveEvidence(e.RootElement);}
Check(noveltyBudget.ShouldFinalize(0)&&noveltyBudget.StopReason=="no_new_evidence","two retrievals without new evidence trigger convergence");
var readBudget=new ResearchBudget(()=>0);for(int i=0;i<12;i++)Check(readBudget.TryGameQuery("get_npc",i.ToString()),"necessary distinct state read fits within the detail budget");
Check(!readBudget.TryGameQuery("get_inventory","{}")&&readBudget.ShouldFinalize(0),"excess state reads force finalization");
var repeatedReadBudget=new ResearchBudget(()=>0);repeatedReadBudget.TryGameQuery("get_inventory","{}");repeatedReadBudget.TryGameQuery("get_inventory","{}");
Check(!repeatedReadBudget.TryGameQuery("get_inventory","{}"),"repeated state polling cannot keep research alive indefinitely");
var roundBudget=new ResearchBudget(()=>0);Check(!roundBudget.ShouldFinalize(2)&&roundBudget.ShouldFinalize(3),"three tool rounds are followed by final synthesis");
int slowPlanCalls=0;
using(var slowPlanHttp=new HttpClient(new StubHandler(async(_,token)=>{
    if(++slowPlanCalls==1)await Task.Delay(Timeout.Infinite,token);
    return StreamAnswer(Event("不能把未查询草稿当作事实。")+End());
})))
{
    var timer=System.Diagnostics.Stopwatch.StartNew();
    string result=await new DeepSeekClient(new FakeSecret(dummyKey),slowPlanHttp,uncachedKnowledge:(_,_)=>Task.FromResult(liveEvidence))
        .AskAsync(options with{TimeoutSeconds=1},Conversation.BuildSearchMessages("夏季规划"),default,_=>{},new FakeGameQueries(),research:true);
    Check(slowPlanCalls==2&&timer.Elapsed.TotalSeconds<2&&result.Contains("未取得可核实"),"slow planning is canceled early, leaving time for an honest bounded response");
}
int deadlineRound=0;
var partialStream=new GatedStream(Encoding.UTF8.GetBytes(Event("已经核实的部分结论[https://stardewvalleywiki.com/Summer]。")),Encoding.UTF8.GetBytes(End()));
using(var partialHttp=new HttpClient(new StubHandler((_,_)=>{
    if(++deadlineRound==1)return Task.FromResult(StreamAnswer(ToolChunk(Enumerable.Range(0,4).Select(i=>(object)new{index=i,id="partial"+i,type="function",function=new{name="wiki_search",arguments=JsonSerializer.Serialize(new{query="summer "+i})}}).ToArray())+Event("","tool_calls")+"data: [DONE]\n\n"));
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(partialStream)});
})))
{
    try
    {
        string result=await new DeepSeekClient(new FakeSecret(dummyKey),partialHttp,uncachedKnowledge:(q,_)=>Task.FromResult(liveEvidence.Replace("Fixture live fish conditions.",q)))
            .AskAsync(options with{TimeoutSeconds=1},Conversation.BuildSearchMessages("夏季规划"),default,_=>{},new FakeGameQueries(),research:true);
        Check(result.Contains("已经核实的部分结论")&&result.Contains("已到时间预算")&&result.Contains("https://stardewvalleywiki.com/Summer"),"deadline returns sourced partial text instead of discarding an unfinished answer");
    }
    finally{partialStream.Release();}
}

Console.WriteLine($"All {passed} checks passed. No real Key or network was used.");

sealed class FakeSecret(string? key) : ISecretStore
{
    public Task<string> GetKeyAsync(CancellationToken token) => key != null ? Task.FromResult(key)
        : Task.FromException<string>(new InvalidOperationException("请配置 DeepSeek Key。"));
}
sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return response(request, cancellationToken); }
}
sealed class ChunkedContent(byte[] data) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data, 0, data.Length);
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(data));
}

sealed class GatedStream(byte[] prefix, byte[] suffix) : Stream
{
    private readonly TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int position;
    public void Release() => gate.TrySetResult(true);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => position; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (position >= prefix.Length) await gate.Task.WaitAsync(token);
        if (position >= prefix.Length + suffix.Length) return 0;
        buffer.Span[0] = position < prefix.Length ? prefix[position] : suffix[position - prefix.Length];
        position++; return 1;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class FakeGameQueries : IGameQueries
{
    public readonly List<string> Calls = new();
    public Task<string> QueryAsync(string name, string arguments, CancellationToken token)
    {
        Calls.Add(name + arguments);
        return Task.FromResult(name == "get_overview" ? "{\"worldReady\":true,\"time\":920}" : "{\"matches\":[{\"container\":\"wood chest\",\"count\":37}]}");
    }
}
