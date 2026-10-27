using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KanKan.Notes;

namespace KanKan.Memos;

/// <summary>노트 한 장의 편집기.</summary>
public partial class MemoCard : UserControl
{
    /// <summary>문단 위아래 간격.</summary>
    public static readonly Thickness ParagraphMargin = new(0, 2, 0, 2);

    /// <summary>입력이 이만큼 멈추면 내용을 저장 형식으로 바꿔 알린다.</summary>
    private static readonly TimeSpan ContentChangedDelay = TimeSpan.FromMilliseconds(300);

    private bool _loading;

    /// <summary>알리지 않은 변경이 있는 동안 돈다.</summary>
    private readonly DispatcherTimer _contentTimer = new() { Interval = ContentChangedDelay };

    public MemoCard(Note note)
    {
        InitializeComponent();
        Note = note;
        _contentTimer.Tick += (_, _) => Flush();

        Editor.Document.PagePadding = new Thickness(0);
        DataObject.AddPastingHandler(Editor, MemoPaste.OnPasting);
        _loading = true;
        MemoFormatting.Load(Editor.Document, note.Xaml);
        MemoImages.Restore(Editor.Document);
        _loading = false;
        UpdatePlaceholder();
    }

    /// <summary>
    /// 영역의 텍스트 설정(글자 크기·행간)을 적용한다. 저장된 노트에 들어 있던 당시 크기·간격도 이 값으로 맞춘다.
    /// 보기 설정이라 노트 내용이 바뀐 것으로 치지 않는다(수정 시각이 바뀌지 않게).
    /// </summary>
    public void ApplyTextStyle(double fontSize, double lineSpacing)
    {
        double lineHeight = Math.Round(fontSize * lineSpacing, 1);
        _loading = true;
        Editor.FontSize = fontSize;
        Placeholder.FontSize = fontSize;
        MemoFormatting.ApplyBaseFont(Editor.Document, Editor.FontFamily, fontSize);
        MemoFormatting.ApplyLineSpacing(Editor.Document, lineHeight, ParagraphMargin);
        _loading = false;

        // 앞으로 Enter로 새로 생기는 문단도 같은 간격으로
        var paragraphStyle = new Style(typeof(System.Windows.Documents.Paragraph));
        paragraphStyle.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, ParagraphMargin));
        paragraphStyle.Setters.Add(new Setter(System.Windows.Documents.Block.LineHeightProperty, lineHeight));
        Editor.Resources[typeof(System.Windows.Documents.Paragraph)] = paragraphStyle;
    }

    public Note Note { get; }

    /// <summary>내용이 바뀌었다. 인자는 저장할 XAML과 글자만 뽑은 내용(제목용).</summary>
    public event Action<MemoCard, string, string>? ContentChanged;

    public bool IsEmpty => MemoFormatting.IsEmpty(Editor.Document);

    public void FocusEditor()
    {
        Editor.Focus();
        Editor.CaretPosition = Editor.Document.ContentEnd;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if (_loading) return;
        // 문서 전체를 XAML로 바꾸는 일은 글 길이에 비례해 느리다. 한글 입력은 글자 하나에도 여러 번 바뀌므로
        // 키마다 하면 긴 메모에서 입력이 버벅인다 → 입력이 멈춘 뒤 한 번만
        _contentTimer.Stop();
        _contentTimer.Start();
    }

    /// <summary>아직 알리지 않은 변경이 있으면 바로 알린다 (노트를 바꾸거나 저장하기 직전).</summary>
    public void Flush()
    {
        if (!_contentTimer.IsEnabled) return;
        _contentTimer.Stop();
        ContentChanged?.Invoke(this, MemoFormatting.Save(Editor.Document), MemoFormatting.PlainText(Editor.Document));
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Flush();

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            MemoFormatting.ToggleStrikethrough(Editor);
            e.Handled = true;
        }
        else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            MemoFormatting.ToggleChecklist(Editor);
            e.Handled = true;
        }
        else if (mods == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = MemoFormatting.HandleEnter(Editor);
        }
    }

    private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: false);
        if (MemoFormatting.TryToggleBoxAt(position)) e.Handled = true;
    }

    /// <summary>편집기가 휠을 삼키지 않고 바깥 스크롤로 넘기도록.</summary>
    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = this,
        });
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = IsEmpty ? Visibility.Visible : Visibility.Collapsed;
}
