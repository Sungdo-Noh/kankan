using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KanKan.Desktop;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Notes;

/// <summary>
/// 메모 영역의 ⋯ → 노트 목록. 노트 보관함의 노트를 최근 순으로 보여주고, 고르면 이 영역에 연다.
/// 다른 메모 영역에 열려 있는 노트는 두 곳에서 동시에 고치지 않도록 열 수 없게 표시한다.
/// </summary>
public partial class NoteListWindow : Window
{
    private static NoteListWindow? s_current;

    private readonly Zone _zone;
    private readonly NoteLibrary _notes;

    /// <summary>삭제 확인 창이 떠 있는 동안에는 포커스를 잃어도 닫지 않는다.</summary>
    private bool _confirming;

    /// <summary>닫히는 중. 창이 닫힐 때도 Deactivated가 오는데, 그때 Close를 또 부르면 예외가 나서 앱이 죽는다.</summary>
    private bool _closing;

    private NoteListWindow(Zone zone)
    {
        InitializeComponent();
        _zone = zone;
        _notes = zone.Notes;
        Closing += (_, _) => _closing = true;
        Refresh();
    }

    public static void ShowFor(Zone zone)
    {
        s_current?.Close();
        var window = new NoteListWindow(zone);
        s_current = window;
        window.Closed += (_, _) => { if (s_current == window) s_current = null; };
        window.Show();
        PopupPlacement.PlaceBeside(window, zone.Bounds);
        window.Activate();
    }

    private void Refresh()
    {
        NoteList.Items.Clear();
        foreach (var note in _notes.Notes.Where(n => !n.IsEmpty || n.Id == _zone.NoteId))
            NoteList.Items.Add(CreateItem(note));
        EmptyText.Visibility = NoteList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ListBoxItem CreateItem(Note note)
    {
        var owner = _notes.FindOwner?.Invoke(note.Id);
        bool isHere = owner == _zone;
        bool openElsewhere = owner != null && !isHere && owner.Mode == ZoneMode.Memo;

        var title = new TextBlock
        {
            Text = note.IsEmpty ? "(빈 노트)" : note.Title,
            FontSize = 13,
            FontWeight = isHere ? FontWeights.Bold : FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var date = new TextBlock
        {
            Text = FormatDate(note.Updated),
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(date, Dock.Right);
        var firstLine = new DockPanel();
        firstLine.Children.Add(date);
        firstLine.Children.Add(title);

        var content = new StackPanel();
        content.Children.Add(firstLine);
        var status = isHere ? "지금 이 영역" : owner != null ? $"'{owner.Title}' 영역에 열려 있음" : null;
        if (status != null)
        {
            content.Children.Add(new TextBlock
            {
                Text = status,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = isHere ? new SolidColorBrush(_zone.Color) : Brushes.Gray,
            });
        }
        else if (note.DeletedZone is { } deleted)
        {
            content.Children.Add(DeletedZoneStatus(deleted));
        }

        var item = new ListBoxItem { Content = content, IsEnabled = !openElsewhere };
        if (openElsewhere) item.ToolTip = "다른 메모 영역에서 보고 있는 노트라 여기서는 열 수 없어요.";
        else if (note.DeletedZone != null) item.ToolTip = "지운 메모 영역을 원래 자리·색 그대로 다시 열어요.";
        else if (!isHere) item.ToolTip = "옆에 새 메모 영역으로 열어요.";
        item.PreviewMouseLeftButtonUp += (_, _) => Open(note);

        var delete = new MenuItem { Header = "노트 삭제" };
        delete.Click += (_, _) => Delete(note);
        item.ContextMenu = new ContextMenu { Items = { delete } };
        return item;
    }

    /// <summary>
    /// 고른 노트는 언제나 새 메모 영역으로 연다. 이 영역의 메모 내용은 바꾸지 않는다.
    /// (지운 메모 영역의 노트면 그 영역이 원래 자리에 되살아나고, 아니면 이 영역 옆에 새로 생긴다)
    /// </summary>
    private void Open(Note note)
    {
        Close();
        var owner = _notes.FindOwner?.Invoke(note.Id);
        if (owner == _zone) return; // 이미 이 영역에 열려 있다

        // 영역 모드라 노트를 안 보여주는 영역이 갖고 있던 노트면 그 영역에서 가져온다
        owner?.ReleaseNote();
        _notes.OpenInNewZone?.Invoke(note, _zone);
    }

    /// <summary>"■ 지운 영역 '제목'" — 영역 색 점과 함께.</summary>
    private static StackPanel DeletedZoneStatus(ZoneData deleted)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        line.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ZonePalette.Parse(deleted.Color)),
        });
        line.Children.Add(new TextBlock { Text = $"지운 영역 '{deleted.Title}'", FontSize = 11, Foreground = Brushes.Gray });
        return line;
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _notes.OpenInNewZone?.Invoke(_notes.Create(), _zone); // 새 노트도 이 영역 옆 새 메모 영역에
    }

    private void Delete(Note note)
    {
        _confirming = true;
        var answer = MessageBox.Show(this, $"'{(note.IsEmpty ? "빈 노트" : note.Title)}' 노트를 삭제할까요?\n삭제한 노트는 되돌릴 수 없어요.",
            "칸칸", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        _confirming = false;
        if (answer != MessageBoxResult.OK) return;

        var owner = _notes.FindOwner?.Invoke(note.Id);
        if (owner == _zone) _zone.OpenNote(_notes.Create()); // 지금 보던 노트면 빈 노트로 바꾼다
        else owner?.ReleaseNote();
        _notes.Delete(note);
        Refresh();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_confirming && !_closing && IsLoaded) Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }


    private static string FormatDate(DateTime time)
    {
        var now = DateTime.Now;
        if (time.Date == now.Date) return time.ToString("HH:mm");
        if (time.Year == now.Year) return time.ToString("M/d HH:mm");
        return time.ToString("yyyy-M-d");
    }
}
