using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KanKan.Memos;

/// <summary>
/// 메모 편집기(RichTextBox)의 서식 명령과 저장 형식.
/// 체크리스트는 문단 맨 앞의 ☐/☑ 글자로 표현한다. 글자라서 저장·복사가 단순하고, 클릭하면 체크가 바뀐다.
/// </summary>
internal static class MemoFormatting
{
    public const char Unchecked = '☐'; // ☐
    public const char Checked = '☑';   // ☑
    private static readonly string NewItemPrefix = Unchecked + " ";

    public static void ToggleBold(RichTextBox editor) => EditingCommands.ToggleBold.Execute(null, editor);

    public static void ToggleBullets(RichTextBox editor) => EditingCommands.ToggleBullets.Execute(null, editor);

    public static void ToggleStrikethrough(RichTextBox editor)
    {
        var selection = editor.Selection;
        var has = HasStrikethrough(selection.GetPropertyValue(Inline.TextDecorationsProperty));
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }

    public static void SetForeground(RichTextBox editor, Brush brush) =>
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);

    /// <summary>형광펜. null이면 지운다.</summary>
    public static void SetHighlight(RichTextBox editor, Brush? brush) =>
        editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, brush);

    #region 체크리스트

    /// <summary>선택한 문단들을 체크리스트로 만들거나, 모두 체크리스트면 해제한다.</summary>
    public static void ToggleChecklist(RichTextBox editor)
    {
        var paragraphs = SelectedParagraphs(editor).ToList();
        if (paragraphs.Count == 0) return;

        bool remove = paragraphs.All(IsChecklist);
        foreach (var p in paragraphs)
        {
            if (remove) RemoveBox(p);
            else if (!IsChecklist(p)) p.ContentStart.GetInsertionPosition(LogicalDirection.Forward).InsertTextInRun(NewItemPrefix);
        }
    }

    /// <summary>체크 박스 글자를 클릭했으면 체크를 바꾸고 true.</summary>
    public static bool TryToggleBoxAt(TextPointer? position)
    {
        if (position?.Paragraph is not { } p || FirstRun(p) is not { } run || !IsChecklist(p)) return false;

        int offset = run.ContentStart.GetOffsetToPosition(position);
        if (offset is not (0 or 1)) return false;

        SetChecked(p, !IsChecked(p));
        return true;
    }

    /// <summary>
    /// 체크리스트 항목에서 Enter: 빈 항목이면 체크리스트를 끝내고(true 반환),
    /// 아니면 기본 줄바꿈 뒤에 새 문단에 빈 체크 박스를 붙인다.
    /// </summary>
    public static bool HandleEnter(RichTextBox editor)
    {
        var p = editor.CaretPosition.Paragraph;
        if (p == null || !IsChecklist(p)) return false;

        if (new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length <= 1)
        {
            RemoveBox(p);
            return true;
        }

        editor.Dispatcher.BeginInvoke(() =>
        {
            var next = editor.CaretPosition.Paragraph;
            if (next == null || next == p || IsChecklist(next)) return;

            next.ContentStart.GetInsertionPosition(LogicalDirection.Forward).InsertTextInRun(NewItemPrefix);
            // 체크된 항목에서 나눴으면 취소선 서식을 물려받으므로, 새 항목은 체크 안 된 모양으로 되돌린다
            new TextRange(next.ContentStart, next.ContentEnd)
                .ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
            if (FirstRun(next) is { } run)
                editor.CaretPosition = run.ContentStart.GetPositionAtOffset(Math.Min(2, run.Text.Length)) ?? editor.CaretPosition;
            editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        });
        return false;
    }

    private static bool IsChecklist(Paragraph p) =>
        FirstRun(p)?.Text is { Length: > 0 } t && (t[0] == Unchecked || t[0] == Checked);

    private static bool IsChecked(Paragraph p) => FirstRun(p)?.Text is { Length: > 0 } t && t[0] == Checked;

    private static void SetChecked(Paragraph p, bool isChecked)
    {
        var run = FirstRun(p)!;
        run.Text = (isChecked ? Checked : Unchecked) + run.Text[1..];

        // 체크 박스 뒤의 내용에 취소선
        var afterBox = run.ContentStart.GetPositionAtOffset(Math.Min(2, run.Text.Length));
        if (afterBox == null) return;
        new TextRange(afterBox, p.ContentEnd).ApplyPropertyValue(Inline.TextDecorationsProperty,
            isChecked ? TextDecorations.Strikethrough : new TextDecorationCollection());
    }

    private static void RemoveBox(Paragraph p)
    {
        var run = FirstRun(p);
        if (run == null) return;
        var text = run.Text;
        run.Text = text.Length > 1 && text[1] == ' ' ? text[2..] : text[1..];
    }

    private static Run? FirstRun(Paragraph p)
    {
        var inline = p.Inlines.FirstInline;
        while (inline is Span span) inline = span.Inlines.FirstInline;
        return inline as Run;
    }

    private static IEnumerable<Paragraph> SelectedParagraphs(RichTextBox editor)
    {
        var p = editor.Selection.Start.Paragraph;
        var end = editor.Selection.End.Paragraph;
        while (p != null)
        {
            yield return p;
            if (p == end) yield break;
            var next = p.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward)?.Paragraph;
            if (next == p) yield break;
            p = next;
        }
    }

    #endregion

    #region 저장 형식

    public static string Save(FlowDocument document)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Load(FlowDocument document, string xaml)
    {
        if (string.IsNullOrEmpty(xaml)) return;
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
            range.Load(stream, DataFormats.Xaml);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Windows.Markup.XamlParseException)
        {
            Log.Write($"메모 불러오기 실패: {ex.Message}");
        }
    }

    public static void ApplyBaseFont(FlowDocument document, FontFamily family, double size)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
    }

    /// <summary>모든 문단(목록 안 문단 포함)의 줄 높이와 문단 간격을 맞춘다.</summary>
    public static void ApplyLineSpacing(FlowDocument document, double lineHeight, Thickness paragraphMargin)
    {
        foreach (var paragraph in Paragraphs(document.Blocks))
        {
            paragraph.LineHeight = lineHeight;
            paragraph.Margin = paragraphMargin;
        }
    }

    private static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    yield return p;
                    break;
                case Section s:
                    foreach (var inner in Paragraphs(s.Blocks)) yield return inner;
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                        foreach (var inner in Paragraphs(item.Blocks)) yield return inner;
                    break;
            }
        }
    }

    public static string PlainText(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text;

    /// <summary>글자가 하나도 없는지. 키를 누를 때마다 부르므로 문서 전체를 글자로 바꾸지 않고 첫 글자를 찾으면 멈춘다.</summary>
    public static bool IsEmpty(FlowDocument document)
    {
        for (var p = document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            switch (p.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text when !string.IsNullOrWhiteSpace(p.GetTextInRun(LogicalDirection.Forward)):
                case TextPointerContext.EmbeddedElement:
                    return false;
                case TextPointerContext.ElementStart when p.GetAdjacentElement(LogicalDirection.Forward) is ListItem:
                    return false; // 글머리 기호만 있어도 빈 메모가 아니다
            }
        }
        return true;
    }

    #endregion

    private static bool HasStrikethrough(object value) =>
        value is TextDecorationCollection decorations &&
        decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);
}
