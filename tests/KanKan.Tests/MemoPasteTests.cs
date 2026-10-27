using System.Windows;
using System.Windows.Controls;
using KanKan.Memos;

namespace KanKan.Tests;

public class MemoPasteTests
{
    [Fact]
    public void PasteFromOtherProgram_KeepsOnlyText() => StaTest.Run(() =>
    {
        var data = new DataObject();
        data.SetText("굵은 글", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Rtf, @"{\rtf1 {\b 굵은 글}}");

        var e = Paste(data);

        Assert.Equal(DataFormats.UnicodeText, e.FormatToApply);
        Assert.False(e.DataObject.GetDataPresent(DataFormats.Rtf));
        Assert.Equal("굵은 글", e.DataObject.GetData(DataFormats.UnicodeText));
    });

    [Fact]
    public void PasteFromMemo_KeepsFormatting() => StaTest.Run(() =>
    {
        var data = new DataObject();
        data.SetText("굵은 글", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Xaml, "<Section/>");

        var e = Paste(data);

        Assert.Same(data, e.DataObject);
        Assert.Equal(DataFormats.Xaml, e.FormatToApply);
    });

    [Fact]
    public void PasteExcelTable_BecomesAlignedText() => StaTest.Run(() =>
    {
        var data = new DataObject();
        data.SetText("이름\t수량\r\n바나나\t12\r\n", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Rtf, @"{\rtf1 \trowd ...}");

        var text = (string)Paste(data).DataObject.GetData(DataFormats.UnicodeText);

        var lines = text.Split("\r\n");
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain('\t', text);
        Assert.Equal("이름", lines[0].Split(' ')[0]);
        Assert.EndsWith("수량", lines[0]);
        Assert.EndsWith("12", lines[1]);
    });

    private static DataObjectPastingEventArgs Paste(DataObject data)
    {
        var format = data.GetDataPresent(DataFormats.Xaml) ? DataFormats.Xaml : DataFormats.Rtf;
        var e = new DataObjectPastingEventArgs(data, isDragDrop: false, format);
        MemoPaste.OnPasting(new RichTextBox(), e);
        return e;
    }

    [Fact]
    public void ParseTsv_SplitsExcelCopy_AndDropsTrailingEmptyRow()
    {
        var rows = MemoPaste.ParseTsv("이름\t수량\r\n사과\t3\r\n\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "이름", "수량" }, rows[0]);
        Assert.Equal(new[] { "사과", "3" }, rows[1]);
    }

    [Fact]
    public void ParseTsv_JoinsLineBreaksInsideQuotedCell()
    {
        var rows = MemoPaste.ParseTsv("\"첫 줄\n둘째 \"\"줄\"\"\"\t끝\r\n");

        Assert.Equal(new[] { "첫 줄 둘째 \"줄\"", "끝" }, Assert.Single(rows));
    }

    [Fact]
    public void ParseTsv_KeepsQuoteInsideUnquotedCell()
    {
        Assert.Equal(new[] { "5\" 모니터", "2" }, Assert.Single(MemoPaste.ParseTsv("5\" 모니터\t2\r\n")));
    }

    [Fact]
    public void Align_PadsEachColumnToWidestCellPlusGap()
    {
        // 글자 하나 = 폭 1 이면 글자 수로 맞춘 것과 같다
        var rows = new[]
        {
            new[] { "이름", "수량", "비고" },
            new[] { "바나나", "12", "" },
            new[] { "배", "", "끝" },
        };

        var text = MemoPaste.Align(rows, s => s.Length, 1);

        Assert.Equal(
            "이름   수량  비고\r\n" +
            "바나나  12\r\n" +
            "배        끝", text);
    }

    [Fact]
    public void Align_UsesMeasuredWidths_SoWideCharactersGetFewerSpaces()
    {
        // 한글은 폭 2, 나머지는 폭 1
        static double Width(string s) => s.Sum(c => c >= '가' ? 2.0 : 1.0);
        var rows = new[]
        {
            new[] { "가나", "x" },
            new[] { "ab", "y" },
        };

        var lines = MemoPaste.Align(rows, Width, 1).Split("\r\n");

        Assert.Equal("가나  x", lines[0]);
        Assert.Equal("ab    y", lines[1]);
    }
}
