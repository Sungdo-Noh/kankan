using KanKan.Storage;

namespace KanKan.Notes;

/// <summary>노트 한 장. 메모 영역은 이 중 하나를 보여준다.</summary>
public sealed class Note
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Xaml { get; set; } = "";
    /// <summary>목록에 보여줄 제목 (내용의 첫 줄). 비어 있으면 빈 노트.</summary>
    public string Title { get; set; } = "";
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;

    /// <summary>이 노트를 보여주던 메모 영역을 지웠으면 그 영역. 노트 목록에서 열면 이 영역을 그대로 되살린다.</summary>
    public ZoneData? DeletedZone { get; set; }

    public bool IsEmpty => Title.Length == 0;
}

/// <summary>
/// 노트 보관함. 모든 메모 영역의 노트가 여기 저장되고, 영역을 지워도 노트는 남는다.
/// 어느 영역에도 열려 있지 않은 빈 노트는 저장할 때 정리한다.
/// </summary>
public sealed class NoteLibrary
{
    private const int MaxTitleLength = 40;

    private readonly List<Note> _notes = new();

    public NoteLibrary() { }

    public NoteLibrary(IEnumerable<NoteData> notes)
    {
        _notes.AddRange(notes.Select(n => new Note
        {
            Id = n.Id, Xaml = n.Xaml, Title = n.Title, Created = n.Created, Updated = n.Updated, DeletedZone = n.DeletedZone,
        }));
    }

    /// <summary>노트가 추가·수정·삭제됐다.</summary>
    public event Action? Changed;

    /// <summary>노트를 갖고 있는 영역을 찾는다 (ZoneManager가 채운다). 없으면 null.</summary>
    internal Func<Guid, Zones.Zone?>? FindOwner { get; set; }

    /// <summary>
    /// 노트를 새 메모 영역으로 연다 (ZoneManager가 채운다). 인자는 노트와 노트 목록을 연 영역.
    /// 노트 목록에서 고른 노트는 언제나 새 영역에 열리고, 지금 보던 메모는 바뀌지 않는다.
    /// </summary>
    internal Action<Note, Zones.Zone>? OpenInNewZone { get; set; }

    /// <summary>최근에 고친 순서.</summary>
    public IReadOnlyList<Note> Notes => _notes.OrderByDescending(n => n.Updated).ToList();

    public Note? Get(Guid? id) => id is { } value ? _notes.FirstOrDefault(n => n.Id == value) : null;

    public Note Create(string xaml = "", string plainText = "")
    {
        var note = new Note { Xaml = xaml, Title = TitleFrom(plainText) };
        _notes.Add(note);
        Changed?.Invoke();
        return note;
    }

    public void Update(Note note, string xaml, string plainText)
    {
        if (note.Xaml == xaml) return;
        note.Xaml = xaml;
        note.Title = TitleFrom(plainText);
        note.Updated = DateTime.Now;
        Changed?.Invoke();
    }

    public void Delete(Note note)
    {
        if (_notes.Remove(note)) Changed?.Invoke();
    }

    /// <summary>
    /// 영역을 지울 때 그 영역의 노트 정리. 글이 없는 노트는 남기지 않고,
    /// 메모 영역이었으면 영역 모습(제목·색·위치·크기 등)을 노트에 남겨 나중에 그대로 되살릴 수 있게 한다.
    /// </summary>
    public void KeepForDeletedZone(ZoneData zone)
    {
        if (Get(zone.NoteId) is not { } note) return;
        if (note.IsEmpty) Delete(note);
        else if (zone.Mode == "memo")
        {
            note.DeletedZone = zone;
            Changed?.Invoke();
        }
    }

    /// <summary>저장할 목록. 어느 영역에도 열려 있지 않은 빈 노트는 빼고 버린다.</summary>
    public List<NoteData> ToData(ISet<Guid> openNoteIds)
    {
        _notes.RemoveAll(n => n.IsEmpty && !openNoteIds.Contains(n.Id));
        return _notes.Select(n => new NoteData
        {
            Id = n.Id, Title = n.Title, Xaml = n.Xaml, Created = n.Created, Updated = n.Updated, DeletedZone = n.DeletedZone,
        }).ToList();
    }

    /// <summary>내용의 첫 번째 글자가 있는 줄. 체크 박스(☐/☑)는 뺀다.</summary>
    public static string TitleFrom(string plainText)
    {
        var line = plainText.Split('\n')
            .Select(l => l.Trim().TrimStart('☐', '☑').Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > MaxTitleLength ? line[..MaxTitleLength] + "…" : line;
    }
}
