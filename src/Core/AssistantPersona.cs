using System.Text.RegularExpressions;

namespace StardewCodexChat.Core;

/// <summary>Player-facing voice only; task rules and tool permissions stay in their existing owners.</summary>
public static class AssistantPersona
{
    public const string Voice = "你是Stardew Copilot，熟悉星露谷、温柔活泼的小伙伴；亲切轻松，偶尔俏皮，不刻意卖萌、说教或扮演下属。称呼固定用「老大」，自然适度，事实、追问和指令可省略；不固定句首，同条不重复，相邻回复避免连用。语气词、Emoji不每条添加。提醒用亲切口语，避免命令口吻，可用「记得…哦」「…啦」；句式灵活，不逐句称呼，不添无关情绪、建议或物品拟人。无固定开场、客服收尾。人格只影响玩家可见措辞，事实、来源、权限和机器格式优先。";

    public const string PlayerText = "以中文为主体，可保留英文专名。简单事实直接说清，通常1–3句；复杂规划按需展开，不固定结构。用普通文本，不用Markdown标题、列表、加粗、斜体、代码、表格、引用块或Markdown链接；来源标记遵守当前模式。";

    public const string Planning = "日常规划先核实再选2–3件重点：临近截止、易错过窗口、今天适合的未完事项及明确目标，保留时间、物品、地点等限制；仅完整规划或多项紧急限时时展开。不复述全部状态，不旁列无关喜好、建筑条件。";

    public const string Dialogue = "事实直答，追问只补新增信息；分享成果一起开心，不接下一步攻略。吐槽先简短理解，有可靠实用的挽回办法可告知，机制仍须检索；不长篇安慰或说教。玩家累了想休息就尊重，不改派轻松任务。准备行动时接住意图，仅重要易遗漏事项轻提醒；已拒绝的不反复催促，可选优化不说成紧急。额外信息仅在不提醒易错过重要事项时简短补充，不堆“可能有用”的知识。不给固定开场、重复总结或客服问句，情绪融入原句，不为卖萌加句。闲聊不转攻略，仅必要澄清或自然延续才提问。只在相关时沿用有效目标、偏好和待办，不刻意展示记忆，不编造经历或未读状态；忘了就坦诚，不表现占有、嫉妒或依赖。" + Planning;

    // A narrow local routing hint, not an emotion classifier or a new model call.
    // Only skips mandatory preflight for explicit small talk, sharing or a plain action intention.
    // Read-only state and Wiki tools remain available; mechanism claims still need evidence.
    public static bool AllowsOptionalKnowledgeLookup(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.StartsWith('/') || Regex.IsMatch(text,
            @"[?？]|怎么|如何|哪|什么|多少|几|吗|么|啥|是否|是不是|会不会|能否|能不能|为什么|为啥|建议|攻略|规划|计划|安排|帮我|请帮|记下|记录|待办|查询|检索|搜索|查一下|告诉我|介绍|比较|推荐|值得|好用|合适|该|值不值|赚不赚|划算|收益|用途|条件|配方|需要|\b(how|what|where|when|why|can|should|plan|help|search|find|compare)\b",
            RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(text, @"^我(?:准备|打算)(?:直接)?(?:出门|出门钓鱼|去钓鱼|去海边钓鱼)(?:了)?[。！!～~]*$")) return true;
        return Regex.IsMatch(text, @"(?:今天|我).{0,10}玩得.{0,5}(?:累|疲惫)|(?:想|打算|准备).{0,4}(?:休息|歇会|歇一歇)|不想(?:干活|工作|做任务)|"
            + @"^(你好[呀啊！!。～~]*|嗨[！!。～~]*|谢谢[你呀啦！!。～~]*|晚安[！!。～~]*|早[呀啊！!。～~]*|再见[！!。～~]*)$|(?:终于|总算).{0,24}(?:攒够|升级|完成|打通|通关|钓到|建好|拿到|成功)|(?:今天|这次).{0,20}(?:亏了|亏了一|亏得|白忙|好累|累坏|好开心|好难过)|(?:我|今天).{0,10}(?:好开心|很开心|好难过|好累|有点沮丧)|只想吐槽|陪我聊(?:聊|天)");
    }
}
