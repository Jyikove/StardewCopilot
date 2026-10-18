using System.Globalization;

namespace StardewCodexChat.Core;

/// <summary>Text editor with a caret on Unicode grapheme boundaries.</summary>
public sealed class TextBuffer
{
    public string Text { get; private set; } = "";
    public int Cursor { get; private set; }
    public bool AllSelected { get; private set; }
    public void SelectAll() { AllSelected = Text.Length > 0; Cursor = Text.Length; }
    public void Clear() { Text = ""; Cursor = 0; AllSelected = false; }
    public void Replace(string text) { Clear(); Insert(text); }

    public void MoveTo(int position)
    {
        position = Math.Clamp(position, 0, Text.Length);
        Cursor = position == Text.Length ? position
            : StringInfo.ParseCombiningCharacters(Text).LastOrDefault(i => i <= position);
        AllSelected = false;
    }
    public void MoveLeft()
    {
        if (AllSelected) { MoveTo(0); return; }
        MoveTo(StringInfo.ParseCombiningCharacters(Text).LastOrDefault(i => i < Cursor));
    }
    public void MoveRight()
    {
        if (AllSelected) { MoveTo(Text.Length); return; }
        MoveTo(StringInfo.ParseCombiningCharacters(Text).FirstOrDefault(i => i > Cursor, Text.Length));
    }

    public void Insert(string value)
    {
        if (AllSelected) Clear();
        value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        value = new string(value.Where(c => !char.IsControl(c) || c == '\n' || c == '\t').ToArray())
            .Replace('\t', ' ');
        int available = Conversation.MaxInputLength - Text.Length;
        if (value.Length > available)
        {
            int length = StringInfo.ParseCombiningCharacters(value).LastOrDefault(i => i <= available);
            value = value[..length];
        }
        int insertedEnd = Cursor + value.Length;
        Text = Text.Insert(Cursor, value);
        // An inserted combining mark or ZWJ may join the neighbouring grapheme.
        Cursor = StringInfo.ParseCombiningCharacters(Text).FirstOrDefault(i => i >= insertedEnd, Text.Length);
        AllSelected = false;
    }

    public void Backspace()
    {
        if (AllSelected) { Clear(); return; }
        if (Cursor == 0) return;
        int start = StringInfo.ParseCombiningCharacters(Text).Last(i => i < Cursor);
        Text = Text.Remove(start, Cursor - start);
        MoveTo(start);
    }
    public void Delete()
    {
        if (AllSelected) { Clear(); return; }
        if (Cursor == Text.Length) return;
        int end = StringInfo.ParseCombiningCharacters(Text).FirstOrDefault(i => i > Cursor, Text.Length);
        Text = Text.Remove(Cursor, end - Cursor);
        MoveTo(Cursor);
    }
}
