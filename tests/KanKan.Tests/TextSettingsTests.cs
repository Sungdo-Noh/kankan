using System.Windows.Documents;
using KanKan.Memos;
using KanKan.Notes;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Tests;

public class TextSettingsTests
{
    [Fact]
    public void SetMemoText_ClampsRoundsAndSaves()
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Mode = "memo" }, new NoteLibrary());
        int changed = 0;
        zone.Changed += () => changed++;

        zone.SetMemoText(40.4, 1.84);

        Assert.Equal(Zone.MaxMemoFontSize, zone.MemoFontSize);
        Assert.Equal(1.8, zone.MemoLineSpacing);
        Assert.Equal(1, changed);
        Assert.Equal((28.0, 1.8), (zone.ToData().MemoFontSize, zone.ToData().MemoLineSpacing));
    }

    [Fact]
    public void OldSaveFile_GetsDefaultTextSettings()
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300 }, new NoteLibrary());
        Assert.Equal((Zone.DefaultMemoFontSize, Zone.DefaultMemoLineSpacing), (zone.MemoFontSize, zone.MemoLineSpacing));
    }

    [Fact]
    public void ApplyTextStyle_ResizesTextAndLines_WithoutTouchingTheNote() => StaTest.Run(() =>
    {
        const string ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var notes = new NoteLibrary();
        var note = notes.Create($"<Section xmlns=\"{ns}\" FontSize=\"13\"><Paragraph>첫 줄</Paragraph><Paragraph>둘째 줄</Paragraph></Section>", "첫 줄");
        var card = new MemoCard(note);
        int contentChanged = 0;
        card.ContentChanged += (_, _, _) => contentChanged++;

        card.ApplyTextStyle(20, 2.0);

        Assert.Equal(20, card.Editor.FontSize);
        var range = new TextRange(card.Editor.Document.ContentStart, card.Editor.Document.ContentEnd);
        Assert.Equal(20.0, range.GetPropertyValue(TextElement.FontSizeProperty));
        Assert.All(card.Editor.Document.Blocks.OfType<Paragraph>(), p => Assert.Equal(40, p.LineHeight));
        Assert.Equal(0, contentChanged); // 보기 설정이라 노트 수정으로 치지 않는다
    });
}
