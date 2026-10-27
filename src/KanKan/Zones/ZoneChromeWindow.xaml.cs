using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KanKan.Desktop;
using KanKan.Memos;
using KanKan.Notes;
using static KanKan.Native.NativeMethods;
using WinForms = System.Windows.Forms;

namespace KanKan.Zones;

public partial class ZoneChromeWindow : Window
{
    private const double EdgeSize = 8;

    // Segoe Fluent/MDL2 아이콘 글꼴의 핀(빈) / 꽂힌 핀(채움)
    private const string PinFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const double PinFontSize = 13; // XAML의 PinButton과 같아야 한다
    private static readonly string PinGlyph = ((char)0xE718).ToString();
    private static readonly string PinnedGlyph = ((char)0xE842).ToString();

    /// <summary>띄움 단계 아이콘: 빈 핀에서 침만 잘라낸 모양. 글리프와 같은 크기의 칸에 그려 위치가 그대로다.</summary>
    private static readonly (Geometry Shape, Size Size) FloatingPin = CreateFloatingPin();

    /// <summary>XAML의 Outline 여백·모서리와 같아야 한다. 색 채움 창이 이 값에 맞춰 잘린다.</summary>
    public const double OutlineInset = 4;
    public const double OutlineRadius = 8;

    /// <summary>메모 판 안쪽 위의 제목 바, 아래의 서식 툴바 높이(DIP). XAML의 Header·MemoToolbar 높이와 같아야 한다.</summary>
    public const double MemoHeaderBand = 32;
    public const double MemoToolbarBand = 40;

    [Flags]
    private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private readonly Zone _zone;

    private bool _dragging;
    private Edge _dragEdges;
    private POINT _dragStartCursor;
    private PixelRect _dragStartBounds;

    /// <summary>서식 툴바가 적용될 메모(마지막으로 포커스를 받은 메모).</summary>
    private MemoCard? _activeCard;

    /// <summary>이 창에서 연 메뉴 수. 메뉴로 포커스가 옮겨가도 그동안은 제목 바·툴바를 숨기지 않는다.</summary>
    private int _openMenus;

    private bool _closingFromZone;

    private static readonly (string Name, Color Color)[] TextColors =
    {
        ("기본", Color.FromRgb(0x22, 0x22, 0x22)),
        ("빨강", Color.FromRgb(0xD9, 0x30, 0x25)),
        ("주황", Color.FromRgb(0xE8, 0x71, 0x0A)),
        ("초록", Color.FromRgb(0x18, 0x80, 0x38)),
        ("파랑", Color.FromRgb(0x1A, 0x73, 0xE8)),
        ("보라", Color.FromRgb(0x93, 0x34, 0xE6)),
        ("회색", Color.FromRgb(0x80, 0x86, 0x8B)),
    };

    private static readonly (string Name, Color Color)[] HighlightColors =
    {
        ("노랑", Color.FromRgb(0xFF, 0xF4, 0x75)),
        ("초록", Color.FromRgb(0xCC, 0xFF, 0x90)),
        ("하늘", Color.FromRgb(0xAE, 0xCB, 0xFA)),
        ("분홍", Color.FromRgb(0xFD, 0xCF, 0xE8)),
        ("주황", Color.FromRgb(0xFF, 0xD8, 0xA8)),
    };

    public ZoneChromeWindow(Zone zone)
    {
        InitializeComponent();
        _zone = zone;
        SetupMemoScrollBar();
        ApplyAppearance();
        UpdatePinButton();
        ApplyMode();
    }

    /// <summary>영역의 제목·색이 바뀌면 다시 칠한다.</summary>
    public void ApplyAppearance()
    {
        var c = _zone.Color;
        Header.Background = new SolidColorBrush(Color.FromArgb(0xE6, c.R, c.G, c.B));
        Outline.BorderBrush = new SolidColorBrush(c);

        var text = ZonePalette.IsLight(c) ? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)) : Brushes.White;
        TitleText.Foreground = text;
        PinButton.Foreground = text;
        MenuButton.Foreground = text;
        NewZoneButton.Foreground = text;
        TitleText.Text = _zone.Title;
        Title = _zone.Title; // 띄운 창의 작업 표시줄·Alt+Tab 이름

        // 메모 판: 영역 색을 흰색 쪽으로 많이 섞은 파스텔
        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.82);
        var pastel = new SolidColorBrush(Color.FromArgb(0xF7, Mix(c.R), Mix(c.G), Mix(c.B)));
        MemoPanel.Background = pastel;
        MemoToolbar.Background = pastel;
    }

    /// <summary>영역 모드 ↔ 메모 모드 화면 전환.</summary>
    public void ApplyMode()
    {
        bool memo = _zone.Mode == ZoneMode.Memo;
        MemoPanel.Visibility = memo ? Visibility.Visible : Visibility.Collapsed;

        FlushMemo(); // 보던 노트의 마지막 입력을 넣고 나서 바꾼다
        MemoList.Children.Clear();
        _activeCard = null;
        if (memo && _zone.Note is { } note) AddCard(note);
        UpdateMemoToolbar();
        UpdatePinButton(); // 띄움 단계는 메모 모드에만 있어서 도움말이 모드에 따라 다르다
    }

    /// <summary>영역이 창을 닫는다(삭제·다시 붙이기·종료).</summary>
    public void CloseFromZone()
    {
        _closingFromZone = true;
        Close();
    }

    /// <summary>
    /// 띄운 메모 창은 보통 창이라 Alt+F4로 닫힐 수 있는데, 그러면 영역은 남고 창만 사라지므로 막는다.
    /// (앱 종료 때는 WPF가 취소를 무시하고 닫는다)
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closingFromZone) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>포커스가 이 창 안에서 옮겨 다니는 중간 상태에 반응하지 않도록, 포커스가 자리 잡은 뒤에 판단한다.</summary>
    private void UpdateMemoToolbarLater() =>
        Dispatcher.BeginInvoke(UpdateMemoToolbar, System.Windows.Threading.DispatcherPriority.Input);

    /// <summary>
    /// 메모 모드: 제목 바는 항상 위에 있고, 서식 툴바는 메모를 편집하는 동안(포커스가 이 창 안에 있거나
    /// 메뉴가 열려 있는 동안)만 메모 안쪽 아래에 나타난다. 영역 크기는 그대로이고, 툴바가 있는 동안은
    /// 글 영역이 그만큼 줄어 마지막 줄이 가려지지 않는다.
    /// </summary>
    private void UpdateMemoToolbar()
    {
        bool memo = _zone.Mode == ZoneMode.Memo;
        bool editing = memo && (Root.IsKeyboardFocusWithin || _openMenus > 0);

        MemoToolbar.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        MemoScroll.Margin = new Thickness(0, MemoHeaderBand, 0, editing ? MemoToolbarBand : 0);
    }

    private void Root_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateMemoToolbarLater();

    #region 메모 스크롤바

    /// <summary>얇은 막대 위에 마우스를 이만큼 올려 두어야 일반 스크롤바로 바뀐다.</summary>
    private static readonly TimeSpan FullScrollBarHoverDelay = TimeSpan.FromSeconds(1);

    /// <summary>마우스가 떠난 뒤 일반 스크롤바를 유지하는 시간.</summary>
    private static readonly TimeSpan FullScrollBarHold = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _fullScrollBarTimer = new() { Interval = FullScrollBarHoverDelay };
    private readonly DispatcherTimer _thinScrollBarTimer = new() { Interval = FullScrollBarHold };

    /// <summary>
    /// 메모 스크롤바: 평소에는 얇은 막대(ThinScrollBar 스타일), 마우스를 1초 올려 두면 일반 스크롤바,
    /// 마우스가 떠나고 5초 뒤 다시 얇은 막대.
    /// </summary>
    private void SetupMemoScrollBar()
    {
        MemoScroll.ApplyTemplate();
        var bar = (ScrollBar)MemoScroll.Template.FindName("PART_VerticalScrollBar", MemoScroll);
        var thinStyle = bar.Style;
        Closed += (_, _) =>
        {
            _fullScrollBarTimer.Stop();
            _thinScrollBarTimer.Stop();
        };

        bar.MouseEnter += (_, _) =>
        {
            _thinScrollBarTimer.Stop();
            if (bar.Style == thinStyle) _fullScrollBarTimer.Start();
        };
        bar.MouseLeave += (_, _) =>
        {
            _fullScrollBarTimer.Stop(); // 1초가 되기 전에 떠나면 얇은 막대 그대로
            if (bar.Style == thinStyle) return;
            _thinScrollBarTimer.Stop();
            _thinScrollBarTimer.Start();
        };
        _fullScrollBarTimer.Tick += (_, _) =>
        {
            _fullScrollBarTimer.Stop();
            bar.Style = null; // 기본(일반) 스크롤바
        };
        _thinScrollBarTimer.Tick += (_, _) =>
        {
            _thinScrollBarTimer.Stop();
            // 모양이 바뀌는 순간 잘못 들어온 MouseLeave일 수 있으니, 아직 올라가 있으면 다시 기다린다
            if (bar.IsMouseOver) _thinScrollBarTimer.Start();
            else bar.Style = thinStyle;
        };
    }

    #endregion

    /// <summary>메뉴를 연다. 열려 있는 동안 메모의 서식 툴바가 사라지지 않게 센다.</summary>
    private void OpenMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => _openMenus++;
        menu.Closed += (_, _) =>
        {
            _openMenus--;
            UpdateMemoToolbarLater();
        };
        menu.IsOpen = true;
    }

    #region 이동·크기 조절

    private void Root_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithin(e.OriginalSource, MenuButton) || IsWithin(e.OriginalSource, PinButton) ||
            IsWithin(e.OriginalSource, NewZoneButton)) return;
        if (IsWithin(e.OriginalSource, TitleEditor)) return;

        if (e.ClickCount == 2 && IsWithin(e.OriginalSource, TitleText))
        {
            BeginRename();
            e.Handled = true;
            return;
        }

        if (_zone.IsLocked) return;

        var edges = HitEdges(e.GetPosition(Root));
        if (edges == Edge.None && !IsWithin(e.OriginalSource, Header)) return;

        _dragging = true;
        _dragEdges = edges;
        GetCursorPos(out _dragStartCursor);
        _dragStartBounds = _zone.Bounds;
        if (edges != Edge.None) FreezeMemoWidth(true);
        Root.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// 크기 조절 중에는 메모 글 너비를 고정해 둔다. 너비가 바뀔 때마다 긴 메모 전체를 다시 줄바꿈하면
    /// 드래그가 버벅여서, 놓을 때 한 번만 새 너비에 맞춘다.
    /// </summary>
    private void FreezeMemoWidth(bool freeze)
    {
        if (freeze && MemoPanel.Visibility == Visibility.Visible)
        {
            MemoList.Width = MemoList.ActualWidth;
            MemoList.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            MemoList.ClearValue(WidthProperty);
            MemoList.ClearValue(HorizontalAlignmentProperty);
        }
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            Root.Cursor = _zone.IsLocked ? null : CursorFor(HitEdges(e.GetPosition(Root)));
            return;
        }
        ApplyDrag();
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        // 놓기 직전의 마지막 이동이 MouseMove로 오지 않을 수 있어 최종 위치를 한 번 더 반영
        ApplyDrag();
        Root.ReleaseMouseCapture();
        _zone.FinishEdit();
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        FreezeMemoWidth(false);
    }

    private void ApplyDrag()
    {
        GetCursorPos(out var now);
        int dx = now.X - _dragStartCursor.X;
        int dy = now.Y - _dragStartCursor.Y;
        var b = _dragStartBounds;

        if (_dragEdges == Edge.None)
        {
            _zone.SetBounds(b with { X = b.X + dx, Y = b.Y + dy });
            return;
        }

        int left = b.X, top = b.Y, right = b.Right, bottom = b.Bottom;
        if (_dragEdges.HasFlag(Edge.Left)) left = Math.Min(left + dx, right - Zone.MinWidth);
        if (_dragEdges.HasFlag(Edge.Right)) right += dx;
        if (_dragEdges.HasFlag(Edge.Top)) top = Math.Min(top + dy, bottom - Zone.MinHeight);
        if (_dragEdges.HasFlag(Edge.Bottom)) bottom += dy;
        _zone.SetBounds(new PixelRect(left, top, right - left, bottom - top));
    }

    #endregion

    #region 이름 바꾸기

    private void BeginRename()
    {
        TitleEditor.Text = _zone.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (TitleEditor.Visibility != Visibility.Visible) return;
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (commit) _zone.Rename(TitleEditor.Text);
    }

    private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EndRename(commit: true);
        else if (e.Key == Key.Escape) EndRename(commit: false);
        else return;
        e.Handled = true;
    }

    private void TitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndRename(commit: true);

    #endregion

    #region 버튼·메뉴

    /// <summary>자유 → 고정 → (메모 모드면) 띄움 → 자유.</summary>
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _zone.SetPin(_zone.Pin switch
        {
            PinState.Free => PinState.Locked,
            PinState.Locked when _zone.Mode == ZoneMode.Memo => PinState.Floating,
            _ => PinState.Free,
        });
        UpdatePinButton();
    }

    /// <summary>지금 단계의 아이콘과, 누르면 무엇이 되는지 알려주는 도움말.</summary>
    private void UpdatePinButton()
    {
        bool memo = _zone.Mode == ZoneMode.Memo;
        (PinButton.Content, PinButton.ToolTip, PinButton.Opacity) = _zone.Pin switch
        {
            PinState.Locked => ((object)PinnedGlyph, memo ? "창으로 띄우기 (브라우저 등 다른 창 위에, Alt+Tab으로 전환)" : "고정 해제", 1.0),
            PinState.Floating => (FloatingPinIcon(), "바탕화면에 다시 붙이기 (고정 해제)", 1.0),
            _ => (PinGlyph, "위치·크기 고정", 0.7),
        };
    }

    private System.Windows.Shapes.Path FloatingPinIcon()
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = FloatingPin.Shape,
            Width = FloatingPin.Size.Width,
            Height = FloatingPin.Size.Height,
        };
        icon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = PinButton });
        return icon;
    }

    /// <summary>빈 핀 글리프의 외곽선에서, 머리의 왼쪽 아래 변 바깥으로 뻗은 침을 잘라낸다.</summary>
    private static (Geometry, Size) CreateFloatingPin()
    {
        var text = new FormattedText(PinGlyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(PinFont), PinFontSize, Brushes.Black, 1.0);
        var glyph = text.BuildGeometry(new Point(0, 0));
        var b = glyph.Bounds;
        Point At(double x, double y) => new(b.X + b.Width * x, b.Y + b.Height * y);

        var keep = new PathGeometry(new[]
        {
            new PathFigure(At(0, 0), new[] { new PolyLineSegment(new[] { At(1, 0), At(1, 1), At(0.60, 1), At(0, 0.43) }, true) }, true),
        });
        var head = Geometry.Combine(glyph, keep, GeometryCombineMode.Intersect, null);
        head.Freeze();
        return (head, new Size(text.WidthIncludingTrailingWhitespace, text.Height));
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MenuButton };

        var toMemo = _zone.Mode == ZoneMode.Zone;
        var mode = new MenuItem { Header = toMemo ? "메모 모드로 바꾸기" : "영역 모드로 바꾸기" };
        mode.Click += (_, _) => _zone.SetMode(toMemo ? ZoneMode.Memo : ZoneMode.Zone);
        menu.Items.Add(mode);
        if (!toMemo)
        {
            var notes = new MenuItem { Header = "노트 목록…" };
            notes.Click += (_, _) => NoteListWindow.ShowFor(_zone);
            menu.Items.Add(notes);

            var text = new MenuItem { Header = "텍스트 설정…" };
            text.Click += (_, _) => TextSettingsWindow.ShowFor(_zone);
            menu.Items.Add(text);
        }
        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "이름 바꾸기" };
        rename.Click += (_, _) => BeginRename();
        menu.Items.Add(rename);

        menu.Items.Add(BuildColorMenu());
        menu.Items.Add(BuildOpacityMenu());
        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "영역 삭제" };
        delete.Click += (_, _) =>
        {
            var answer = MessageBox.Show($"'{_zone.Title}' 영역을 삭제할까요?\n(바탕화면의 파일·폴더는 그대로 남아요)",
                "칸칸", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.OK) _zone.RequestDelete();
        };
        menu.Items.Add(delete);

        OpenMenu(menu);
    }

    private MenuItem BuildColorMenu()
    {
        var root = new MenuItem { Header = "색 변경" };
        foreach (var (name, color) in ZonePalette.Colors)
        {
            var item = new MenuItem { Header = name, Icon = Swatch(color), IsChecked = color == _zone.Color };
            item.Click += (_, _) => _zone.SetColor(color);
            root.Items.Add(item);
        }
        root.Items.Add(new Separator());

        var custom = new MenuItem { Header = "직접 고르기…" };
        custom.Click += (_, _) => PickCustomColor();
        root.Items.Add(custom);
        return root;
    }

    private MenuItem BuildOpacityMenu()
    {
        var root = new MenuItem { Header = "배경 진하기" };
        foreach (var (name, opacity) in ZonePalette.Opacities)
        {
            var item = new MenuItem { Header = name, IsChecked = Math.Abs(opacity - _zone.Opacity) < 0.01 };
            item.Click += (_, _) => _zone.SetOpacity(opacity);
            root.Items.Add(item);
        }
        return root;
    }

    private void PickCustomColor()
    {
        var c = _zone.Color;
        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B),
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            _zone.SetColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    #endregion

    #region 메모

    /// <summary>노트를 연 직후 바로 쓸 수 있게 편집기에 포커스를 준다.</summary>
    public void FocusMemo() =>
        Dispatcher.BeginInvoke(() => MemoList.Children.OfType<MemoCard>().FirstOrDefault()?.FocusEditor(),
            DispatcherPriority.Input);

    /// <summary>편집기에서 아직 노트에 넣지 않은 입력을 넣는다.</summary>
    public void FlushMemo()
    {
        foreach (var card in MemoList.Children.OfType<MemoCard>()) card.Flush();
    }

    /// <summary>영역의 텍스트 설정(글자 크기·행간)이 바뀌었다.</summary>
    public void ApplyMemoText()
    {
        foreach (var card in MemoList.Children.OfType<MemoCard>())
            card.ApplyTextStyle(_zone.MemoFontSize, _zone.MemoLineSpacing);
    }

    private MemoCard AddCard(Note note)
    {
        var card = new MemoCard(note);
        card.ApplyTextStyle(_zone.MemoFontSize, _zone.MemoLineSpacing);
        card.ContentChanged += (c, xaml, text) => _zone.UpdateNote(c.Note, xaml, text);
        card.Editor.GotKeyboardFocus += (_, _) => _activeCard = card;
        MemoList.Children.Add(card);
        return card;
    }

    /// <summary>메모 판의 글 아래 빈 곳을 눌러도 바로 이어 쓸 수 있게 한다.</summary>
    private void MemoPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithin(e.OriginalSource, MemoToolbar)) return;
        if (MemoList.Children.OfType<MemoCard>().Any(card => IsWithin(e.OriginalSource, card))) return;
        if (MemoList.Children.OfType<MemoCard>().LastOrDefault() is not { } last) return;

        last.FocusEditor();
        e.Handled = true;
    }

    private void NewZoneButton_Click(object sender, RoutedEventArgs e) => _zone.RequestNewZoneBeside();

    /// <summary>툴바 명령을 마지막으로 편집하던 메모에 적용하고 포커스를 돌려준다.</summary>
    private void WithActiveEditor(Action<RichTextBox> action)
    {
        var card = _activeCard ?? MemoList.Children.OfType<MemoCard>().FirstOrDefault();
        if (card == null) return;
        action(card.Editor);
        card.Editor.Focus();
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => WithActiveEditor(MemoFormatting.ToggleBold);

    private void Strike_Click(object sender, RoutedEventArgs e) => WithActiveEditor(MemoFormatting.ToggleStrikethrough);

    private void Checklist_Click(object sender, RoutedEventArgs e) => WithActiveEditor(MemoFormatting.ToggleChecklist);

    private void Bullets_Click(object sender, RoutedEventArgs e) => WithActiveEditor(MemoFormatting.ToggleBullets);

    private void TextColor_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TextColorButton };
        foreach (var (name, color) in TextColors)
        {
            var item = new MenuItem { Header = name, Icon = Swatch(color) };
            item.Click += (_, _) => WithActiveEditor(ed => MemoFormatting.SetForeground(ed, new SolidColorBrush(color)));
            menu.Items.Add(item);
        }
        OpenMenu(menu);
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = HighlightButton };
        foreach (var (name, color) in HighlightColors)
        {
            var item = new MenuItem { Header = name, Icon = Swatch(color) };
            item.Click += (_, _) => WithActiveEditor(ed => MemoFormatting.SetHighlight(ed, new SolidColorBrush(color)));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var none = new MenuItem { Header = "형광펜 지우기" };
        none.Click += (_, _) => WithActiveEditor(ed => MemoFormatting.SetHighlight(ed, null));
        menu.Items.Add(none);
        OpenMenu(menu);
    }

    #endregion

    #region 공용

    private static Border Swatch(Color color) => new()
    {
        Width = 14,
        Height = 14,
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(color),
    };

    #endregion

    private Edge HitEdges(Point p)
    {
        var edges = Edge.None;
        if (p.X < EdgeSize) edges |= Edge.Left;
        if (p.X > Root.ActualWidth - EdgeSize) edges |= Edge.Right;
        if (p.Y < EdgeSize) edges |= Edge.Top;
        if (p.Y > Root.ActualHeight - EdgeSize) edges |= Edge.Bottom;
        return edges;
    }

    private static Cursor? CursorFor(Edge edges) => edges switch
    {
        Edge.Left or Edge.Right => Cursors.SizeWE,
        Edge.Top or Edge.Bottom => Cursors.SizeNS,
        Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => Cursors.SizeNWSE,
        Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => Cursors.SizeNESW,
        _ => null,
    };

    private static bool IsWithin(object source, DependencyObject ancestor)
    {
        for (var d = source as DependencyObject; d != null;
             d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == ancestor) return true;
        return false;
    }
}
