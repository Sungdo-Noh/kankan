using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KanKan.Memos;

namespace KanKan.Tests;

public class MemoFormattingTests
{
    public MemoFormattingTests() => Log.Enabled = false;

    [Fact]
    public void ToggleChecklist_AddsBoxesToSelectedParagraphs_ThenRemovesThem() => Sta(() =>
    {
        var editor = Editor("사과", "배");
        editor.SelectAll();

        MemoFormatting.ToggleChecklist(editor);
        Assert.Equal(new[] { "☐ 사과", "☐ 배" }, Texts(editor));

        editor.SelectAll();
        MemoFormatting.ToggleChecklist(editor);
        Assert.Equal(new[] { "사과", "배" }, Texts(editor));
    });

    [Fact]
    public void ClickingBox_TogglesCheckAndStrikesThroughItemText() => Sta(() =>
    {
        var editor = Editor("☐ 우유 사기");
        var p = Paragraphs(editor)[0];

        Assert.True(MemoFormatting.TryToggleBoxAt(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward)));
        Assert.Equal("☑ 우유 사기", Text(p));
        Assert.True(HasStrike(ItemTextRange(p)));

        Assert.True(MemoFormatting.TryToggleBoxAt(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward)));
        Assert.Equal("☐ 우유 사기", Text(p));
        Assert.False(HasStrike(ItemTextRange(p)));
    });

    [Fact]
    public void ClickingItemText_DoesNotToggle() => Sta(() =>
    {
        var editor = Editor("☐ 우유 사기");
        var p = Paragraphs(editor)[0];
        var middle = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetPositionAtOffset(4);

        Assert.False(MemoFormatting.TryToggleBoxAt(middle));
        Assert.Equal("☐ 우유 사기", Text(p));
    });

    [Fact]
    public void EnterOnEmptyItem_EndsChecklist() => Sta(() =>
    {
        var editor = Editor("☐ ");
        editor.CaretPosition = editor.Document.ContentEnd;

        Assert.True(MemoFormatting.HandleEnter(editor));
        Assert.Equal(new[] { "" }, Texts(editor));
    });

    [Fact]
    public void EnterAfterItem_StartsNewUncheckedItem() => Sta(() =>
    {
        var editor = Editor("☑ 끝난 일");
        var p = Paragraphs(editor)[0];
        MemoFormatting.TryToggleBoxAt(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward)); // ☐
        MemoFormatting.TryToggleBoxAt(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward)); // ☑ + 취소선
        editor.CaretPosition = p.ContentEnd;

        Assert.False(MemoFormatting.HandleEnter(editor)); // 기본 줄바꿈은 그대로 진행
        // 화면에 없는 편집기는 Enter 명령을 실행하지 않으므로, 편집기가 하는 일(문단 나누기 + 커서 이동)을 직접 재현
        editor.CaretPosition = editor.CaretPosition.InsertParagraphBreak();
        Pump();

        var paragraphs = Paragraphs(editor);
        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("☐ ", Text(paragraphs[1]));
        Assert.False(HasStrike(new TextRange(paragraphs[1].ContentStart, paragraphs[1].ContentEnd)));
    });

    [Fact]
    public void ToggleStrikethrough_TogglesOnSelection() => Sta(() =>
    {
        var editor = Editor("취소할 글");
        editor.SelectAll();

        MemoFormatting.ToggleStrikethrough(editor);
        Assert.True(HasStrike(editor.Selection));

        MemoFormatting.ToggleStrikethrough(editor);
        Assert.False(HasStrike(editor.Selection));
    });

    [Fact]
    public void SaveAndLoad_KeepsFormatting() => Sta(() =>
    {
        var editor = Editor("굵은 글");
        editor.SelectAll();
        MemoFormatting.ToggleBold(editor);
        MemoFormatting.ToggleStrikethrough(editor);
        MemoFormatting.SetForeground(editor, new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)));
        MemoFormatting.SetHighlight(editor, new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0x75)));

        var xaml = MemoFormatting.Save(editor.Document);
        var loaded = new FlowDocument();
        MemoFormatting.Load(loaded, xaml);

        var range = new TextRange(loaded.ContentStart, loaded.ContentEnd);
        Assert.Equal("굵은 글", range.Text.TrimEnd());
        Assert.Equal(FontWeights.Bold, range.GetPropertyValue(TextElement.FontWeightProperty));
        Assert.True(HasStrike(range));
        Assert.Equal(Color.FromRgb(0xD9, 0x30, 0x25), ((SolidColorBrush)range.GetPropertyValue(TextElement.ForegroundProperty)).Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xF4, 0x75), ((SolidColorBrush)range.GetPropertyValue(TextElement.BackgroundProperty)).Color);
    });

    [Fact]
    public void ApplyBaseFont_UpdatesOldMemoFontSize_ButKeepsBold() => Sta(() =>
    {
        var editor = Editor("예전 메모");
        editor.SelectAll();
        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 13.0);
        MemoFormatting.ToggleBold(editor);
        var doc = new FlowDocument();
        MemoFormatting.Load(doc, MemoFormatting.Save(editor.Document));

        MemoFormatting.ApplyBaseFont(doc, new FontFamily("Malgun Gothic"), 14);

        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        Assert.Equal(14.0, range.GetPropertyValue(TextElement.FontSizeProperty));
        Assert.Equal(FontWeights.Bold, range.GetPropertyValue(TextElement.FontWeightProperty));
    });

    [Fact]
    public void ApplyLineSpacing_ReachesParagraphsInsideLists() => Sta(() =>
    {
        var doc = new FlowDocument(new Paragraph(new Run("문단")) { Margin = new Thickness(0, 1, 0, 1) });
        var list = new List();
        list.ListItems.Add(new ListItem(new Paragraph(new Run("목록 항목"))));
        doc.Blocks.Add(list);

        MemoFormatting.ApplyLineSpacing(doc, 22, new Thickness(0, 2, 0, 2));

        var paragraphs = new[] { (Paragraph)doc.Blocks.FirstBlock, (Paragraph)list.ListItems.FirstListItem.Blocks.FirstBlock };
        Assert.All(paragraphs, p =>
        {
            Assert.Equal(22, p.LineHeight);
            Assert.Equal(new Thickness(0, 2, 0, 2), p.Margin);
        });
    });

    [Fact]
    public void Load_IgnoresBrokenXaml() => Sta(() =>
    {
        var doc = new FlowDocument();
        MemoFormatting.Load(doc, "<깨진 데이터");
        Assert.True(MemoFormatting.IsEmpty(doc));
    });

    [Fact]
    public void IsEmpty_IgnoresBlankLines_ButNotTextOrBullets() => Sta(() =>
    {
        Assert.True(MemoFormatting.IsEmpty(new FlowDocument()));
        Assert.True(MemoFormatting.IsEmpty(Editor("", "  \t", "").Document));
        Assert.False(MemoFormatting.IsEmpty(Editor("", "  ", "글").Document));

        var bullets = new List();
        bullets.ListItems.Add(new ListItem(new Paragraph()));
        Assert.False(MemoFormatting.IsEmpty(new FlowDocument(bullets)));
    });

    #region 도우미

    private static RichTextBox Editor(params string[] paragraphs)
    {
        var doc = new FlowDocument();
        foreach (var text in paragraphs) doc.Blocks.Add(new Paragraph(new Run(text)));
        return new RichTextBox(doc);
    }

    private static List<Paragraph> Paragraphs(RichTextBox editor) => editor.Document.Blocks.OfType<Paragraph>().ToList();

    private static string Text(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text;

    private static string[] Texts(RichTextBox editor) => Paragraphs(editor).Select(Text).ToArray();

    /// <summary>체크 박스("☐ ") 뒤의 항목 글자 범위.</summary>
    private static TextRange ItemTextRange(Paragraph p) =>
        new(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetPositionAtOffset(2)!, p.ContentEnd);

    private static bool HasStrike(TextRange range) =>
        range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection c &&
        c.Any(d => d.Location == TextDecorationLocation.Strikethrough);

    /// <summary>BeginInvoke로 미뤄 둔 작업을 실행시킨다.</summary>
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void Sta(Action test) => StaTest.Run(test);

    #endregion
}

internal static class StaTest
{
    /// <summary>WPF 컨트롤은 STA 스레드에서만 만들 수 있다.</summary>
    public static void Run(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
