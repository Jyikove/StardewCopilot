namespace StardewCodexChat.Core;

public sealed record CommandSuggestion(string Name,string Description,bool NeedsQuestion=false);

/// <summary>Presentation state only. Completing a command never executes it.</summary>
public sealed class CommandCompletion
{
    public static IReadOnlyList<CommandSuggestion> Entries { get; } = Array.AsReadOnly(new[]{
        new CommandSuggestion("/search","深度思考与实时联网检索",true),
        new CommandSuggestion("/screen","截图分析当前游戏画面",true),
        new CommandSuggestion("/help","询问助手的功能与用法",true),
        new CommandSuggestion("/clearchat","清除当前存档的全部对话"),
        new CommandSuggestion("/cleartodo","清除当前存档的全部待办"),
        new CommandSuggestion("/resetkey","重新配置 API Key")
    });
    public bool Visible {get;private set;}
    public int Selected {get;private set;}
    private bool wasEligible;
    public void Update(string text,int cursor,bool enabled)
    {
        bool eligible=enabled&&text=="/"&&cursor==1;
        if(!eligible){Visible=false;Selected=0;}
        else if(!wasEligible){Visible=true;Selected=0;}
        wasEligible=eligible;
    }
    public void Move(int direction)
    {
        if(Visible)Selected=(Selected+direction%Entries.Count+Entries.Count)%Entries.Count;
    }
    public void Dismiss()=>Visible=false;
    public string? Complete(int? index=null)
    {
        if(!Visible)return null;
        var entry=Entries[Math.Clamp(index??Selected,0,Entries.Count-1)];
        Visible=false;
        return entry.Name+(entry.NeedsQuestion?" ":"");
    }
}
