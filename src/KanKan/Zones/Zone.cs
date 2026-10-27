using System.Windows.Interop;
using System.Windows.Media;
using KanKan.Desktop;
using KanKan.Notes;
using KanKan.Storage;
using static KanKan.Native.NativeMethods;

namespace KanKan.Zones;

/// <summary>
/// 하나의 영역. 두 장의 창으로 이루어진다.
/// - 색 채움: 아이콘 뒤에 깔리는 단색 반투명 창.
/// - 크롬(제목 바·테두리): 아이콘 위에서 이동·크기 조절·메뉴 클릭을 받는다. 안쪽은 완전 투명이라 클릭이 아이콘으로 통과한다.
///   메모 모드에서는 크롬 안쪽에 메모 판이 깔려 글을 쓸 수 있다.
///   메모 영역을 띄우면(핀 3단계) 크롬 창만 바탕화면에서 떨어진 보통 창이 된다.
/// 창들은 탐색기 창의 자식이라 탐색기가 재시작되면 파괴된다. 그때는 <see cref="Detach"/> 후 <see cref="Attach"/>로 다시 만든다.
/// </summary>
public sealed class Zone
{
    public const int MinWidth = 160;
    public const int MinHeight = 100;
    public const int MaxTitleLength = 40;

    private ZoneChromeWindow? _chrome;
    private FillWindow? _fill;
    private IntPtr _chromeHwnd;
    private PinState _pin;
    private bool _visible = true;

    public Zone(ZoneData data, NoteLibrary notes)
    {
        Notes = notes;
        Id = data.Id;
        Title = data.Title;
        Color = ZonePalette.Parse(data.Color);
        Opacity = Math.Clamp(data.Opacity, 0.05, 1.0);
        Bounds = new PixelRect(data.X, data.Y, data.Width, data.Height);
        Mode = data.Mode == "memo" ? ZoneMode.Memo : ZoneMode.Zone;
        _pin = data.Floating && Mode == ZoneMode.Memo ? PinState.Floating
             : data.Locked ? PinState.Locked : PinState.Free;
        NoteId = data.NoteId;
        MemoFontSize = Math.Clamp(data.MemoFontSize, MinMemoFontSize, MaxMemoFontSize);
        MemoLineSpacing = Math.Clamp(data.MemoLineSpacing, MinMemoLineSpacing, MaxMemoLineSpacing);
        if (Mode == ZoneMode.Memo) EnsureNote();
    }

    public const double MinMemoFontSize = 10, MaxMemoFontSize = 28, DefaultMemoFontSize = 14;
    public const double MinMemoLineSpacing = 1.0, MaxMemoLineSpacing = 2.5, DefaultMemoLineSpacing = 1.6;

    /// <summary>메모 글자 크기(DIP). 메모 영역마다 따로.</summary>
    public double MemoFontSize { get; private set; }

    /// <summary>메모 행간 (글자 크기의 배수).</summary>
    public double MemoLineSpacing { get; private set; }

    /// <summary>텍스트 설정: 글자 크기·행간을 바꾸고 바로 메모에 반영한다.</summary>
    public void SetMemoText(double fontSize, double lineSpacing)
    {
        fontSize = Math.Clamp(Math.Round(fontSize), MinMemoFontSize, MaxMemoFontSize);
        lineSpacing = Math.Clamp(Math.Round(lineSpacing, 1), MinMemoLineSpacing, MaxMemoLineSpacing);
        if (fontSize == MemoFontSize && lineSpacing == MemoLineSpacing) return;
        MemoFontSize = fontSize;
        MemoLineSpacing = lineSpacing;
        _chrome?.ApplyMemoText();
        Changed?.Invoke();
    }

    /// <summary>색 구역 / 스티커 메모.</summary>
    public ZoneMode Mode { get; private set; }

    /// <summary>노트 보관함 (모든 영역이 함께 쓴다).</summary>
    public NoteLibrary Notes { get; }

    /// <summary>메모 모드에서 보여주는 노트. 영역 모드로 바꿔도 기억해 두었다가 다시 보여준다.</summary>
    public Guid? NoteId { get; private set; }

    public Note? Note => Notes.Get(NoteId);

    public Guid Id { get; }
    public string Title { get; private set; }
    public Color Color { get; private set; }

    /// <summary>배경(색 채움) 진하기 0~1.</summary>
    public double Opacity { get; private set; }

    public PixelRect Bounds { get; private set; }

    private byte FillAlpha => (byte)Math.Round(Opacity * 255);

    /// <summary>
    /// 핀 단계. 고정이면 제목 바·테두리를 드래그해도 이동·크기 조절이 되지 않는다.
    /// 띄움(메모 모드만)이면 바탕화면에서 떼어 보통 창이 된다 — 브라우저 등 다른 창 위로 올라오고 Alt+Tab으로 오간다.
    /// </summary>
    public PinState Pin => _pin;

    public bool IsLocked => _pin == PinState.Locked;

    public bool IsFloating => _pin == PinState.Floating;

    public void SetPin(PinState pin)
    {
        if (pin == PinState.Floating && Mode != ZoneMode.Memo) pin = PinState.Free;
        if (_pin == pin) return;
        bool rehost = (pin == PinState.Floating) != IsFloating;
        _pin = pin;
        if (rehost) Rehost(activate: pin == PinState.Floating);
        Changed?.Invoke();
    }

    /// <summary>저장해야 할 상태(위치·크기·고정 등)가 바뀌었다. 연달아 오므로 모아서 저장한다.</summary>
    public event Action? Changed;

    /// <summary>드래그 이동·크기 조절을 끝냈다. 바로 저장한다.</summary>
    public event Action? EditFinished;

    public event Action<Zone>? DeleteRequested;

    /// <summary>제목 바의 + : 이 영역 옆에 같은 크기·다른 색의 새 영역을 만들어 달라.</summary>
    public event Action<Zone>? NewZoneBesideRequested;

    public void Attach() => Attach(activate: false);

    /// <summary>창을 만든다. activate면 (띄운 창일 때) 앞으로 가져와 포커스를 준다.</summary>
    public void Attach(bool activate)
    {
        _fill = DesktopHost.CreateFillBehindIcons(Color, FillAlpha);

        _chrome = new ZoneChromeWindow(this);
        if (IsFloating)
        {
            // 바탕화면에 붙이지 않은 보통 창: 다른 창 위로 올라오고 작업 표시줄·Alt+Tab에 나온다
            _chrome.ShowInTaskbar = true;
            _chromeHwnd = new WindowInteropHelper(_chrome).EnsureHandle();
            Place(); // 기본 위치에 잠깐 나타났다 옮겨지지 않게 보이기 전에 놓는다
            _chrome.Show();
        }
        else
        {
            _chromeHwnd = new WindowInteropHelper(_chrome).EnsureHandle();
            DesktopHost.AttachAboveIcons(_chromeHwnd);
            _chrome.Show();
        }

        Place();
        UpdateVisibility();
        if (activate && _visible) _chrome.Activate();
    }

    /// <summary>창을 정리한다. 탐색기와 함께 이미 파괴된 창이어도 안전하다.</summary>
    public void Detach()
    {
        FlushMemo();
        if (_chrome != null && IsWindow(_chromeHwnd)) _chrome.CloseFromZone();
        _chrome = null;
        _fill?.Dispose();
        _fill = null;
    }

    /// <summary>
    /// 바탕화면에 붙인 창 ↔ 띄운 보통 창은 크롬 창을 새로 만들어 바꾼다.
    /// 핀 버튼·메뉴의 클릭 처리 도중에 그 창을 닫지 않도록 처리가 끝난 뒤에 한다.
    /// </summary>
    private void Rehost(bool activate)
    {
        _chrome?.Dispatcher.BeginInvoke(() =>
        {
            if (_chrome == null) return; // 그사이 지워졌다
            Detach();
            Attach(activate);
        });
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateVisibility();
    }

    /// <summary>
    /// 영역 ↔ 메모 전환. 메모 판은 아이콘 위에 그려지므로 영역 안 아이콘은 메모 아래에 그 자리 그대로 가려져 있다가,
    /// 영역 모드로 돌아오면 다시 보인다 (아이콘은 옮기지 않는다).
    /// </summary>
    public void SetMode(ZoneMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        if (mode == ZoneMode.Memo) EnsureNote();
        if (mode == ZoneMode.Zone && IsFloating)
        {
            _pin = PinState.Free; // 띄움은 메모 모드에서만 — 바탕화면으로 돌려놓는다
            Rehost(activate: false);
        }
        UpdateVisibility();
        _chrome?.ApplyMode();
        Changed?.Invoke();
    }

    /// <summary>노트 목록에서 고른 노트(또는 새 노트)를 이 메모 영역에 연다. 원래 노트는 보관함에 남는다.</summary>
    public void OpenNote(Note note)
    {
        if (note.Id == NoteId) return;
        NoteId = note.Id;
        _chrome?.ApplyMode();
        _chrome?.FocusMemo();
        Changed?.Invoke();
    }

    /// <summary>다른 영역이 이 영역의 노트를 가져갔다. 다시 메모 모드가 되면 빈 노트로 시작한다.</summary>
    public void ReleaseNote()
    {
        NoteId = null;
        if (Mode == ZoneMode.Memo)
        {
            EnsureNote();
            _chrome?.ApplyMode();
        }
        Changed?.Invoke();
    }

    /// <summary>노트를 연 직후 바로 쓸 수 있게 메모 편집기에 포커스를 준다.</summary>
    public void FocusMemo() => _chrome?.FocusMemo();

    /// <summary>메모 편집기에 남아 있는(아직 노트에 넣지 않은) 입력을 노트에 넣는다. 저장 직전에 부른다.</summary>
    public void FlushMemo() => _chrome?.FlushMemo();

    /// <summary>메모 편집기에서 내용이 바뀌었다.</summary>
    public void UpdateNote(Note note, string xaml, string plainText) => Notes.Update(note, xaml, plainText);

    /// <summary>보여줄 노트가 없거나 보관함에서 지워졌으면 빈 노트를 새로 만든다.</summary>
    private void EnsureNote()
    {
        if (Note == null) NoteId = Notes.Create().Id;
    }

    public void SetBounds(PixelRect bounds)
    {
        Bounds = bounds with
        {
            Width = Math.Max(MinWidth, bounds.Width),
            Height = Math.Max(MinHeight, bounds.Height),
        };
        Place();
        Changed?.Invoke();
    }

    public void Rename(string title)
    {
        title = title.Trim();
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];
        if (title.Length == 0 || title == Title)
        {
            _chrome?.ApplyAppearance(); // 빈 이름이면 원래 이름으로 되돌려 보여준다
            return;
        }
        Title = title;
        _chrome?.ApplyAppearance();
        Changed?.Invoke();
    }

    public void SetColor(Color color)
    {
        if (color == Color) return;
        Color = color;
        _fill?.SetColor(color);
        _chrome?.ApplyAppearance();
        Changed?.Invoke();
    }

    public void SetOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.05, 1.0);
        if (Math.Abs(opacity - Opacity) < 0.001) return;
        Opacity = opacity;
        _fill?.SetAlpha(FillAlpha);
        Changed?.Invoke();
    }

    public void FinishEdit() => EditFinished?.Invoke();

    public void RequestDelete() => DeleteRequested?.Invoke(this);

    public void RequestNewZoneBeside() => NewZoneBesideRequested?.Invoke(this);

    public ZoneData ToData() => new()
    {
        Id = Id,
        Title = Title,
        Color = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}",
        Opacity = Math.Round(Opacity, 3),
        X = Bounds.X,
        Y = Bounds.Y,
        Width = Bounds.Width,
        Height = Bounds.Height,
        Locked = IsLocked,
        Floating = IsFloating,
        Mode = Mode == ZoneMode.Memo ? "memo" : "zone",
        NoteId = NoteId,
        MemoFontSize = MemoFontSize,
        MemoLineSpacing = MemoLineSpacing,    };

    /// <summary>메모 모드에서는 크롬 창의 메모 판이 배경을 칠하므로 색 채움 창은 숨긴다.</summary>
    private void UpdateVisibility()
    {
        if (_chrome != null && IsWindow(_chromeHwnd)) ShowWindow(_chromeHwnd, _visible ? SW_SHOWNA : SW_HIDE);
        if (_fill != null) ShowWindow(_fill.Handle, _visible && Mode == ZoneMode.Zone ? SW_SHOWNA : SW_HIDE);
    }

    private void Place()
    {
        if (_chrome == null || _fill == null) return;
        DesktopHost.Place(_chromeHwnd, Bounds);

        // 색 채움은 크롬의 테두리(Outline) 안쪽에 딱 맞춘다
        var scale = VisualTreeHelper.GetDpi(_chrome).DpiScaleX;
        int inset = (int)Math.Round(ZoneChromeWindow.OutlineInset * scale);
        int radius = (int)Math.Round(ZoneChromeWindow.OutlineRadius * scale);
        var rect = new PixelRect(Bounds.X + inset, Bounds.Y + inset, Bounds.Width - inset * 2, Bounds.Height - inset * 2);
        DesktopHost.Place(_fill.Handle, rect);
        _fill.SetRoundedShape(rect.Width, rect.Height, radius);
    }
}
