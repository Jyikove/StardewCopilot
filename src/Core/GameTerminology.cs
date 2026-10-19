using System.Text;
using System.Text.RegularExpressions;

namespace StardewCodexChat.Core;

public sealed record GameTerm(string English, string Chinese);

/// <summary>Immutable, save-independent name data. Never translates facts, URLs or tool arguments.</summary>
public sealed class GameTerminology
{
    private Snapshot current = new(Array.Empty<GameTerm>());
    public Snapshot Current => Volatile.Read(ref current);
    public void Replace(IEnumerable<GameTerm> terms) => Volatile.Write(ref current, new Snapshot(terms));

    public sealed class Snapshot
    {
        private readonly Dictionary<string, GameTerm> names = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> prefixes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Regex matcher;
        private readonly Regex duplicateName;
        private readonly int longest;
        public int Count { get; }
        private static string Clean(string text) => Regex.Replace(text.Trim().Replace('’', '\''), @"\s+", " ");
        public Snapshot(IEnumerable<GameTerm> terms)
        {
            // Conflicting translations are deliberately omitted rather than guessed.
            var entries = terms.Where(t => !string.IsNullOrWhiteSpace(t.English) && !string.IsNullOrWhiteSpace(t.Chinese))
                .Select(t => new GameTerm(Clean(t.English), Clean(t.Chinese)))
                .Where(t => t.English.Length is >= 3 and <= 80 && t.Chinese.Length is >= 1 and <= 80
                    && Regex.IsMatch(t.English, @"^[A-Za-z\u00C0-\u024F][A-Za-z0-9\u00C0-\u024F '\-().!+&]*$")
                    && Regex.IsMatch(t.Chinese, @"[\u3400-\u9fff]") && !t.Chinese.Contains('[') && !t.Chinese.Contains('{'))
                .GroupBy(t => t.English, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(t => t.Chinese).Distinct().Count() == 1).Select(g => g.First()).Take(10000).ToArray();
            Count = entries.Length;
            foreach (var term in entries)
            {
                names[term.English] = term;
                // Chinese matching supplies names for Chinese questions too, without changing their text.
                names.TryAdd(term.Chinese, term);
                for (int n = 1; n <= term.English.Length; n++) prefixes.Add(term.English[..n]);
                for (int n = 1; n <= term.Chinese.Length; n++) prefixes.Add(term.Chinese[..n]);
            }
            foreach (string s in new[] { "http://", "https://" })
                for (int n = 1; n <= s.Length; n++) prefixes.Add(s[..n]);
            longest = Math.Max(8, entries.Select(t => Math.Max(t.English.Length, t.Chinese.Length)).DefaultIfEmpty(0).Max());
            string alternatives = string.Join("|", names.Keys.OrderByDescending(s => s.Length)
                .Select(s => Regex.Escape(s).Replace("'", "['’]")));
            matcher = new Regex(@"https?://[^\s\[\]<>]+|(?<![A-Za-z0-9_\u00C0-\u024F])(?:" + (alternatives.Length == 0 ? "(?!)" : alternatives)
                + @")(?![A-Za-z0-9_\u00C0-\u024F])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            string chineseNames = string.Join("|", entries.Select(t => t.Chinese).Distinct().OrderByDescending(s => s.Length).Select(Regex.Escape));
            duplicateName = new Regex(@"https?://[^\s\[\]<>]+|(?<name>" + (chineseNames.Length == 0 ? "(?!)" : chineseNames)
                + @")(?:[ \t]*(?:\([ \t]*\k<name>[ \t]*\)|（[ \t]*\k<name>[ \t]*）))+",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        private bool TryTerm(string text, out GameTerm term) => names.TryGetValue(Clean(text), out term!);
        public string Rewrite(string text)
        {
            if (Count == 0) return text;
            try { text = matcher.Replace(text, m => TryTerm(m.Value, out var term) ? term.Chinese : m.Value); }
            catch (RegexMatchTimeoutException) { return text; } // Never turn valid history into a storage failure.
            try { return duplicateName.Replace(text, m => m.Groups["name"].Success ? m.Groups["name"].Value : m.Value); }
            catch (RegexMatchTimeoutException) { return text; }
        }

        public string Context(string text)
        {
            const string rule = "游戏内中文术语（仅用于译名，不证明物品持有、效果数值或获取条件；机制仍须检索）：已确认的名称直接使用下表中文名，不加原名或别称，不在括号里再重复名字；必要的属性说明可保留。效果解释用中文；未命中且无法确认官方译名时保留英文并简短解释，不杜撰。\n";
            var result = new StringBuilder(rule);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Match match in matcher.Matches(text.Replace('_', ' ')))
                {
                    if (!TryTerm(match.Value, out var term) || !seen.Add(term.English)) continue;
                    string row = term.English + " = " + term.Chinese + "\n";
                    if (seen.Count > 24 || result.Length + row.Length > 1800) break;
                    result.Append(row);
                }
            }
            catch (RegexMatchTimeoutException) { /* Keep any already matched terms; translation is not an extra API. */ }
            return result.ToString();
        }

        public StreamRewriter Stream(Action<string> emit) => new(this, emit);
        private bool PendingNameTail(string tail, GameTerm term)
        {
            while (true)
            {
                tail = tail.TrimStart(' ', '\t');
                if (tail.Length == 0) return true;
                if (tail[0] is not ('(' or '（')) return false;
                int close = tail.IndexOf(tail[0] == '(' ? ')' : '）', 1);
                if (close < 0)
                {
                    string prefix = Clean(tail[1..]);
                    return term.English.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || term.Chinese.StartsWith(prefix, StringComparison.Ordinal);
                }
                if (!TryTerm(tail[1..close], out var repeated) || repeated.Chinese != term.Chinese) return false;
                tail = tail[(close + 1)..];
            }
        }
        public sealed class StreamRewriter(Snapshot owner, Action<string> emit)
        {
            private readonly StringBuilder pending = new();
            private static bool Word(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or >= '\u00c0' and <= '\u024f';
            public void Push(string chunk)
            {
                pending.Append(chunk);
                string text = pending.ToString();
                int end = text.Length;
                // Keep an unfinished English word, multiword name, or URL across arbitrary SSE chunks.
                while (end > 0 && Word(text[end - 1])) end--;
                for (int start = Math.Max(0, text.Length - owner.longest); start < end; start++)
                    if ((start == 0 || !Word(text[start - 1])) && owner.prefixes.Contains(Clean(text[start..]))) end = start;
                foreach (Match url in Regex.Matches(text, @"https?://[^\s\[\]<>]*", RegexOptions.IgnoreCase))
                    if (url.Index + url.Length == text.Length) end = Math.Min(end, url.Index);
                // Keep a trailing name until a possible parenthetical translation is complete.
                // Otherwise separate chunks could emit "钢刀" before "(Steel Falchion)" arrives.
                try
                {
                    foreach (Match match in owner.matcher.Matches(text))
                    {
                        if (match.Index < end && owner.TryTerm(match.Value, out var term)
                            && owner.PendingNameTail(text[(match.Index + match.Length)..], term)) end = match.Index;
                    }
                }
                catch (RegexMatchTimeoutException) { /* A failed optional cleanup must not cancel the answer. */ }
                if (end == 0) return;
                emit(owner.Rewrite(text[..end])); pending.Remove(0, end);
            }
            public void Complete()
            {
                if (pending.Length > 0) emit(owner.Rewrite(pending.ToString()));
                pending.Clear();
            }
        }
    }
}
