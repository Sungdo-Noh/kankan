using System.IO;
using System.Windows.Threading;
using KanKan.Desktop;
using KanKan.Memos;
using KanKan.Notes;
using KanKan.Storage;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;
using Screen = System.Windows.Forms.Screen;
using static KanKan.Native.NativeMethods;

namespace KanKan.Zones;

/// <summary>영역 목록을 만들고·지우고·저장하고, 탐색기 재시작 시 다시 붙인다.</summary>
internal sealed class ZoneManager
{
    private const int DefaultWidthDip = 420;
    private const int DefaultHeightDip = 300;

    private readonly List<Zone> _zones = new();
    private readonly DispatcherTimer _saveTimer;

    /// <summary>마지막으로 삭제한 영역. 새 영역이 위치·크기·색·진하기를 이어받는다.</summary>
    private ZoneData? _lastDeleted;

    /// <summary>모든 메모 영역이 함께 쓰는 노트 보관함.</summary>
    private NoteLibrary _notes = new();

    /// <summary>저장 내용을 만든 순서. 늦게 끝난 백그라운드 저장이 더 새 내용을 덮지 않게 한다.</summary>
    private long _saveSequence;

    public ZoneManager()
    {
        // 드래그 중에는 계속 바뀌므로 멈춘 뒤 한 번만 저장
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => Save();
    }

    public bool IsVisible { get; private set; } = true;

    public int Count => _zones.Count;

    public void Load()
    {
        var data = ZoneStore.Load();
        _notes = new NoteLibrary(data?.Notes ?? new());
        _notes.FindOwner = id => _zones.FirstOrDefault(z => z.NoteId == id);
        _notes.OpenInNewZone = OpenInNewZone;
        _notes.Changed += ScheduleSave;
        // 안 쓰는 사진 정리. 깨져서 보관해 둔 저장 파일이 쓰던 사진은 남긴다 (그 파일을 되살릴 수 있게).
        // 예전 형식의 영역별 메모도 아직 노트로 옮기기 전이라 함께 본다
        if (data != null)
            MemoImages.DeleteUnused(data.Notes.Select(n => n.Xaml)
                .Concat(data.Zones.SelectMany(z => z.Memos ?? new()).Select(m => m.Xaml))
                .Concat(ZoneStore.BrokenBackups()));

        if (data == null)
        {
            // 첫 실행: 예시 영역 하나
            Add(new Zone(NewZoneData(), _notes));
            Save();
            return;
        }

        _lastDeleted = data.LastDeleted is { } deleted ? deleted with { Memos = null, NoteId = null } : null;
        foreach (var zoneData in data.Zones)
            Add(new Zone(KeepOnScreen(MigrateMemos(zoneData, _notes)), _notes));
    }

    /// <summary>
    /// 예전 형식(영역마다 메모 목록)을 노트 보관함으로 옮긴다. 내용이 있는 메모마다 노트를 만들고,
    /// 첫 번째 노트를 그 영역이 보여준다.
    /// </summary>
    internal static ZoneData MigrateMemos(ZoneData data, NoteLibrary notes)
    {
        if (data.Memos is not { Count: > 0 } memos || data.NoteId != null) return data with { Memos = null };

        Guid? first = null;
        foreach (var memo in memos)
        {
            var doc = new System.Windows.Documents.FlowDocument();
            MemoFormatting.Load(doc, memo.Xaml);
            var text = MemoFormatting.PlainText(doc);
            if (text.Trim().Length == 0) continue;
            var note = notes.Create(memo.Xaml, text);
            first ??= note.Id;
        }
        return data with { NoteId = first, Memos = null };
    }

    public void AttachAll()
    {
        foreach (var zone in _zones)
        {
            zone.Attach();
            zone.SetVisible(IsVisible);
        }
    }

    /// <summary>탐색기가 재시작되어 창이 사라졌을 때 다시 만든다.</summary>
    public void ReattachAll()
    {
        foreach (var zone in _zones) zone.Detach();
        AttachAll();
    }

    public void CreateZone()
    {
        if (!IsVisible) SetVisible(true);
        var zone = new Zone(NewZoneData(), _notes);
        Add(zone);
        zone.Attach();
        Save();
    }

    /// <summary>
    /// 제목 바의 + : 원래 영역 옆 빈 자리에 같은 크기·같은 모드·다음 색으로 만든다.
    /// 띄운 메모 영역이면 새 영역도 띄운 창으로 만든다.
    /// </summary>
    private void CreateZoneBeside(Zone source) => AddZoneBeside(source, source.Mode, noteId: null, floating: source.IsFloating);

    /// <summary>
    /// 노트 목록에서 고른 노트를 새 메모 영역으로 연다 (지금 보던 메모는 그대로).
    /// 지운 메모 영역의 노트면 그 영역을 원래대로 되살리고, 아니면 노트 목록을 연 영역 옆에 만든다
    /// (띄운 메모에서 열었으면 새 메모도 띄운 창으로).
    /// </summary>
    private void OpenInNewZone(Note note, Zone beside)
    {
        var zone = note.DeletedZone != null
            ? RestoreZone(note)
            : AddZoneBeside(beside, ZoneMode.Memo, note.Id, floating: beside.IsFloating);
        zone.FocusMemo();
    }

    /// <summary>
    /// 원래 영역 옆 빈 자리에 같은 크기·다음 색으로 만든다. noteId가 있으면 그 노트를 보여준다.
    /// floating이면 (메모 모드만) 바탕화면에서 떨어진 띄운 창으로 만들어 앞으로 가져온다.
    /// </summary>
    private Zone AddZoneBeside(Zone source, ZoneMode mode, Guid? noteId, bool floating = false)
    {
        var src = source.Bounds;
        var srcRect = new Rectangle(src.X, src.Y, src.Width, src.Height);
        var spot = FindSpotBeside(srcRect, Screen.FromRectangle(srcRect).WorkingArea, ZoneRects(), Gap);

        var zone = new Zone(NewZoneData() with
        {
            Color = NextColor(source.Color),
            Opacity = source.Opacity,
            X = spot.X,
            Y = spot.Y,
            Width = spot.Width,
            Height = spot.Height,
            Mode = mode == ZoneMode.Memo ? "memo" : "zone",
            NoteId = noteId,
            Floating = floating,
        }, _notes);
        Add(zone);
        zone.Attach(activate: zone.IsFloating);
        zone.SetVisible(IsVisible);
        Save();
        return zone;
    }

    /// <summary>
    /// 오른쪽 → 아래 → 왼쪽 → 위 순서로, 작업 영역 안이면서 다른 영역과 겹치지 않는 첫 자리.
    /// 그런 자리가 없으면 조금 비켜서 겹쳐 놓는다.
    /// </summary>
    internal static Rectangle FindSpotBeside(Rectangle source, Rectangle workArea, IReadOnlyCollection<Rectangle> others, int gap)
    {
        if (TryFindFreeSpotBeside(source, source.Size, workArea, others, gap, out var spot)) return spot;
        return OffsetSpot(source, workArea, gap);
    }

    /// <summary>anchor의 오른쪽 → 아래 → 왼쪽 → 위에 size 크기로 놓을 수 있는 빈 자리.</summary>
    private static bool TryFindFreeSpotBeside(Rectangle anchor, Size size, Rectangle workArea,
        IReadOnlyCollection<Rectangle> others, int gap, out Rectangle spot)
    {
        Point[] candidates =
        {
            new(anchor.Right + gap, anchor.Y),
            new(anchor.X, anchor.Bottom + gap),
            new(anchor.X - gap - size.Width, anchor.Y),
            new(anchor.X, anchor.Y - gap - size.Height),
        };
        foreach (var location in candidates)
        {
            var candidate = new Rectangle(location, size);
            if (!workArea.Contains(candidate) || others.Any(o => o.IntersectsWith(candidate))) continue;
            spot = candidate;
            return true;
        }
        spot = default;
        return false;
    }

    /// <summary>빈 자리가 없을 때: 조금 비켜서 겹쳐 놓되 작업 영역 안으로.</summary>
    private static Rectangle OffsetSpot(Rectangle source, Rectangle workArea, int gap)
    {
        int x = Math.Clamp(source.X + gap * 3, workArea.X, Math.Max(workArea.X, workArea.Right - source.Width));
        int y = Math.Clamp(source.Y + gap * 3, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - source.Height));
        return new Rectangle(new Point(x, y), source.Size);
    }

    /// <summary>
    /// 마지막으로 삭제한 영역의 위치·크기·색·진하기를 이어받은 새 영역.
    /// 그 자리에 이미 다른 영역이 있으면 원래 자리 옆, 그다음 그 자리를 차지한 영역의 옆에서 빈 곳을 찾는다.
    /// </summary>
    internal static ZoneData InheritFrom(ZoneData last, string title, IReadOnlyCollection<Rectangle> others, Rectangle workArea, int gap)
    {
        var rect = new Rectangle(last.X, last.Y, last.Width, last.Height);
        var blockers = others.Where(o => o.IntersectsWith(rect)).ToList();
        if (blockers.Count > 0)
        {
            var anchors = blockers.Prepend(rect);
            rect = anchors.Select(a => TryFindFreeSpotBeside(a, rect.Size, workArea, others, gap, out var s) ? s : (Rectangle?)null)
                          .FirstOrDefault(s => s != null) ?? OffsetSpot(rect, workArea, gap);
        }

        return new ZoneData
        {
            Title = title,
            Color = last.Color,
            Opacity = last.Opacity,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
        };
    }

    /// <summary>팔레트에서 원래 색의 다음 색. 팔레트에 없는 색이면 그와 다른 첫 색.</summary>
    internal static string NextColor(System.Windows.Media.Color current)
    {
        var colors = ZonePalette.Colors;
        int index = Array.FindIndex(colors, c => c.Color == current);
        return ToHex(index >= 0 ? colors[(index + 1) % colors.Length].Color : colors.First(c => c.Color != current).Color);
    }

    private static string ToHex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        foreach (var zone in _zones) zone.SetVisible(visible);
    }

    public void Shutdown()
    {
        // 저장 예약이 없어도 언제나 기다려서 쓴다: 아직 끝나지 않은 백그라운드 저장이 있거나 앞의 저장이 실패했을 수 있다
        SaveNow();
        foreach (var zone in _zones) zone.Detach();
    }

    private void Add(Zone zone)
    {
        zone.Changed += ScheduleSave;
        zone.EditFinished += Save;
        zone.DeleteRequested += Delete;
        zone.NewZoneBesideRequested += CreateZoneBeside;
        _zones.Add(zone);
    }

    private void Delete(Zone zone)
    {
        zone.Detach();
        zone.Changed -= ScheduleSave;
        zone.EditFinished -= Save;
        zone.DeleteRequested -= Delete;
        zone.NewZoneBesideRequested -= CreateZoneBeside;
        _zones.Remove(zone);
        var data = zone.ToData();
        _notes.KeepForDeletedZone(data);
        _lastDeleted = data with { NoteId = null }; // 노트는 보관함에 남고, 새 영역이 이어받지는 않는다
        Save();
    }

    /// <summary>
    /// 지운 메모 영역을 지울 때의 제목·색·진하기·위치·크기·글자 설정 그대로 다시 만든다.
    /// 위치도 원래 자리 그대로다(다른 영역과 겹치더라도). 화면 밖이 됐으면 주 모니터로 데려온다.
    /// </summary>
    private Zone RestoreZone(Note note)
    {
        var data = note.DeletedZone!;
        note.DeletedZone = null;
        if (!IsVisible) SetVisible(true);

        var zone = new Zone(KeepOnScreen(data with { NoteId = note.Id, Mode = "memo" }), _notes);
        Add(zone);
        zone.Attach(activate: zone.IsFloating); // 띄운 채 지운 메모면 띄운 창으로 앞에 되살린다
        zone.SetVisible(IsVisible);
        if (_lastDeleted?.Id == data.Id) _lastDeleted = null; // 다음 새 영역이 되살린 자리를 이어받지 않게
        Save();
        return zone;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// 지금 내용을 바로 저장한다. 파일 쓰기는 백그라운드에서 한다 — 영역 창은 탐색기 창의 자식이라 입력 큐가 이어져 있어서,
    /// UI 스레드가 디스크를 기다리면 바탕화면까지 멈칫한다.
    /// </summary>
    private void Save()
    {
        var (data, sequence) = Snapshot();
        Task.Run(() => Write(data, sequence));
    }

    /// <summary>파일에 다 쓸 때까지 기다린다. 종료 직전(앱 종료·Windows 종료)에 부른다.</summary>
    public void SaveNow()
    {
        var (data, sequence) = Snapshot();
        Write(data, sequence);
    }

    /// <summary>저장할 내용. 노트·영역을 다루므로 UI 스레드에서 만든다 (만든 뒤에는 바뀌지 않는 값이라 다른 스레드에서 써도 된다).</summary>
    private (StoreData Data, long Sequence) Snapshot()
    {
        foreach (var zone in _zones) zone.FlushMemo();
        _saveTimer.Stop();
        var data = new StoreData
        {
            Zones = _zones.Select(z => z.ToData()).ToList(),
            Notes = _notes.ToData(_zones.Where(z => z.NoteId != null).Select(z => z.NoteId!.Value).ToHashSet()),
            LastDeleted = _lastDeleted,
        };
        return (data, ++_saveSequence);
    }

    private static void Write(StoreData data, long sequence)
    {
        try
        {
            ZoneStore.Save(data, sequence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 새 영역. 삭제한 영역이 있으면 그 위치·크기·색·진하기를 이어받고,
    /// 없으면 주 모니터 작업 영역 가운데에 기존 영역 수만큼 조금씩 비켜서 만든다.
    /// </summary>
    private ZoneData NewZoneData()
    {
        var title = _zones.Count == 0 ? "새 영역" : $"새 영역 {_zones.Count + 1}";
        if (_lastDeleted is { } deleted)
        {
            var last = KeepOnScreen(deleted);
            var lastRect = new Rectangle(last.X, last.Y, last.Width, last.Height);
            return InheritFrom(last, title, ZoneRects(), Screen.FromRectangle(lastRect).WorkingArea, Gap);
        }

        var scale = GetDpiForSystem() / 96.0;
        int width = (int)(DefaultWidthDip * scale);
        int height = (int)(DefaultHeightDip * scale);
        int offset = (int)(_zones.Count % 5 * 32 * scale);
        var area = Screen.PrimaryScreen!.WorkingArea;

        return new ZoneData
        {
            Title = title,
            Color = ToHex(ZonePalette.Colors[_zones.Count % ZonePalette.Colors.Length].Color),
            X = area.X + (area.Width - width) / 2 + offset,
            Y = area.Y + (area.Height - height) / 2 + offset,
            Width = width,
            Height = height,
        };
    }

    private List<Rectangle> ZoneRects() =>
        _zones.Select(z => new Rectangle(z.Bounds.X, z.Bounds.Y, z.Bounds.Width, z.Bounds.Height)).ToList();

    /// <summary>영역 사이 간격(물리 픽셀).</summary>
    private static int Gap => (int)(12 * GetDpiForSystem() / 96.0);

    /// <summary>모니터 구성이 바뀌어 화면 밖에 놓인 영역은 주 모니터로 데려온다.</summary>
    private static ZoneData KeepOnScreen(ZoneData data)
    {
        var rect = new Rectangle(data.X, data.Y, data.Width, data.Height);
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return data;

        var area = Screen.PrimaryScreen!.WorkingArea;
        Log.Write($"영역 '{data.Title}'이(가) 화면 밖이라 주 모니터로 이동");
        return data with { X = area.X + 40, Y = area.Y + 40 };
    }
}
