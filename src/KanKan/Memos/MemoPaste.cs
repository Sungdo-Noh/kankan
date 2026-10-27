using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KanKan.Memos;

/// <summary>
/// 메모 붙여넣기. 메모장(txt)에 붙여넣을 때처럼 바깥 프로그램의 서식(글꼴·크기·줄 간격·색·들여쓰기 등)은 버리고 글자만 넣는다.
/// 메모끼리 복사한 것만 서식(굵게·취소선·형광펜 등)을 그대로 둔다.
/// 엑셀 등에서 복사한 표는 표(Table)로 넣지 않고, 칸 사이를 공백으로 채워 줄을 맞춘 글자로 넣는다.
/// 메모 글꼴은 글자마다 폭이 달라 탭이나 글자 수로는 줄이 어긋나므로, 실제 그려지는 폭을 재서 공백 수를 정한다.
/// </summary>
internal static class MemoPaste
{
    /// <summary>칸과 칸 사이 최소 간격 (공백 몇 개 폭).</summary>
    private const int GapSpaces = 2;

    public static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not RichTextBox editor || IsFromMemo(e.DataObject)) return;

        // 글자가 없으면 사진: 가로 10cm로 맞춰 넣는다. 글자와 사진이 같이 있으면(엑셀 칸 복사 등) 글자를 넣는다
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text)
        {
            var images = MemoImages.FromData(e.DataObject);
            if (images.Count == 0) return;
            MemoImages.Insert(editor, images);
            e.CancelCommand();
            return;
        }

        if (IsTable(e.DataObject) && ParseTsv(text) is { Count: > 0 } rows)
        {
            var measure = Measurer(editor);
            text = Align(rows, measure, measure(" "));
        }

        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        e.DataObject = data;
        e.FormatToApply = DataFormats.UnicodeText;
    }

    /// <summary>WPF 편집기에서 복사한 것(메모끼리 복사)이면 XAML 형식이 같이 들어 있다.</summary>
    private static bool IsFromMemo(IDataObject data) =>
        data.GetDataPresent(DataFormats.XamlPackage) || data.GetDataPresent(DataFormats.Xaml);

    /// <summary>
    /// 칸이 탭으로 나뉜 글자에 표 형식(RTF·HTML·엑셀 형식)이 같이 들어 있으면 표로 본다.
    /// 탭이 든 그냥 글자(메모장에서 복사한 들여쓰기 등)는 그대로 둔다.
    /// </summary>
    private static bool IsTable(IDataObject data) =>
        data.GetData(DataFormats.UnicodeText) is string text && text.Contains('\t') &&
        (data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.Html) ||
         data.GetDataPresent(DataFormats.CommaSeparatedValue) || data.GetDataPresent("XML Spreadsheet"));

    /// <summary>
    /// 탭으로 칸, 줄바꿈으로 행을 나눈다. 엑셀은 칸 안에 줄바꿈이 있으면 칸을 "…"로 감싸므로 그 안의 줄바꿈은 공백으로 바꾼다.
    /// 끝의 빈 행은 버린다.
    /// </summary>
    public static List<List<string>> ParseTsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            // 칸 맨 앞의 따옴표만 감싼 칸의 시작이다
            if (cell.Length == 0 && text[i] == '"' && ClosingQuote(text, i) is int close)
            {
                cell.Append(text[(i + 1)..close].Replace("\"\"", "\"").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '));
                i = close + 1;
                continue;
            }

            char c = text[i];
            if (c == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else cell.Append(c);
            i++;
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace)) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    /// <summary>i의 여는 따옴표와 짝인 닫는 따옴표 위치. 닫는 따옴표 뒤가 칸·행의 끝이 아니면 감싼 칸이 아니다.</summary>
    private static int? ClosingQuote(string text, int i)
    {
        for (int j = i + 1; j < text.Length; j++)
        {
            if (text[j] != '"') continue;
            if (j + 1 < text.Length && text[j + 1] == '"') { j++; continue; }
            return j + 1 == text.Length || text[j + 1] is '\t' or '\r' or '\n' ? j : null;
        }
        return null;
    }

    /// <summary>
    /// 칸마다 그 열의 가장 넓은 칸 + 간격만큼 공백을 채워 다음 칸의 시작 위치를 맞춘다.
    /// 공백 폭 단위로 반올림하되, 앞 칸들의 오차가 쌓이지 않게 지금까지 실제로 채운 폭을 따라간다.
    /// </summary>
    public static string Align(IReadOnlyList<IReadOnlyList<string>> rows, Func<string, double> measure, double spaceWidth)
    {
        var cells = rows.Select(r => r.Select(c => c.Trim()).ToList()).ToList();
        int columns = cells.Max(r => r.Count);

        // 열 시작 위치: 앞 열의 가장 넓은 칸 + 간격
        var starts = new double[columns];
        for (int c = 1; c < columns; c++)
        {
            double widest = cells.Where(r => r.Count > c - 1).Max(r => measure(r[c - 1]));
            starts[c] = starts[c - 1] + widest + GapSpaces * spaceWidth;
        }

        var lines = new List<string>();
        foreach (var row in cells)
        {
            // 뒤쪽 빈 칸은 채울 필요가 없다
            int last = row.FindLastIndex(c => c.Length > 0);
            var line = new StringBuilder();
            double x = 0;
            for (int c = 0; c <= last; c++)
            {
                if (c > 0)
                {
                    int spaces = Math.Max(1, (int)Math.Round((starts[c] - x) / spaceWidth));
                    line.Append(' ', spaces);
                    x += spaces * spaceWidth;
                }
                line.Append(row[c]);
                x += measure(row[c]);
            }
            lines.Add(line.ToString());
        }
        return string.Join("\r\n", lines);
    }

    /// <summary>붙여넣을 자리의 글꼴로 글자 폭을 잰다.</summary>
    private static Func<string, double> Measurer(RichTextBox editor)
    {
        var selection = editor.Selection;
        var typeface = new Typeface(
            selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily ?? editor.FontFamily,
            selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style ? style : editor.FontStyle,
            selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight ? weight : editor.FontWeight,
            editor.FontStretch);
        double size = selection.GetPropertyValue(TextElement.FontSizeProperty) is double s ? s : editor.FontSize;
        double pixelsPerDip = VisualTreeHelper.GetDpi(editor).PixelsPerDip;

        return text => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, size, Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;
    }
}
