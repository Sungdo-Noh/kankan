using KanKan.Notes;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Tests;

public class NoteLibraryTests
{
    public NoteLibraryTests() => Log.Enabled = false;

    [Theory]
    [InlineData("이번 주 할 일\r\n☐ 키워드 조사", "이번 주 할 일")]
    [InlineData("\r\n\r\n  두 번째 줄부터  \r\n", "두 번째 줄부터")]
    [InlineData("☑ 끝난 일\r\n", "끝난 일")]
    [InlineData("   \r\n", "")]
    public void TitleFrom_UsesFirstNonEmptyLine(string text, string expected) =>
        Assert.Equal(expected, NoteLibrary.TitleFrom(text));

    [Fact]
    public void TitleFrom_ShortensLongLines()
    {
        var title = NoteLibrary.TitleFrom(new string('가', 60));
        Assert.Equal(41, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Notes_AreListedMostRecentlyUpdatedFirst()
    {
        var notes = new NoteLibrary();
        var older = notes.Create("a", "먼저 쓴 노트");
        var newer = notes.Create("b", "나중 노트");
        notes.Update(older, "a2", "먼저 쓴 노트 고침");

        Assert.Equal(new[] { older.Id, newer.Id }, notes.Notes.Select(n => n.Id));
        Assert.Equal("먼저 쓴 노트 고침", older.Title);
    }

    [Fact]
    public void Saving_DropsEmptyNotesThatNoZoneShows_ButKeepsTheRest()
    {
        var notes = new NoteLibrary();
        var kept = notes.Create("x", "내용 있는 노트 (영역 삭제됨)");
        var emptyOpen = notes.Create();
        var emptyOrphan = notes.Create();

        var saved = notes.ToData(new HashSet<Guid> { emptyOpen.Id });

        Assert.Equal(new[] { kept.Id, emptyOpen.Id }.OrderBy(x => x), saved.Select(n => n.Id).OrderBy(x => x));
        Assert.Null(notes.Get(emptyOrphan.Id));
    }

    [Fact]
    public void DeletingMemoZone_RemembersZoneOnNote_ToRestoreItLater()
    {
        var notes = new NoteLibrary();
        var note = notes.Create("x", "회의 메모");
        var zone = new ZoneData { Title = "회의", Color = "#2BB3A6", Opacity = 0.15, X = 1109, Y = 111, Width = 581, Height = 827, Mode = "memo", NoteId = note.Id };

        notes.KeepForDeletedZone(zone);

        Assert.Equal(zone, note.DeletedZone);
        var reloaded = new NoteLibrary(notes.ToData(new HashSet<Guid>())).Get(note.Id)!;
        Assert.Equal(zone, reloaded.DeletedZone); // 다시 켜도 기억한다
    }

    [Theory]
    [InlineData("memo")]
    [InlineData("zone")]
    public void DeletingZone_WithEmptyNote_DropsTheNote(string mode)
    {
        var notes = new NoteLibrary();
        var empty = notes.Create("x", "  \r\n☐ \r\n");

        notes.KeepForDeletedZone(new ZoneData { Mode = mode, NoteId = empty.Id });

        Assert.Null(notes.Get(empty.Id));
    }

    [Fact]
    public void DeletingZoneModeZone_KeepsNote_AsOrdinaryNote()
    {
        var notes = new NoteLibrary();
        var note = notes.Create("x", "예전에 메모였던 영역");

        notes.KeepForDeletedZone(new ZoneData { Mode = "zone", NoteId = note.Id });

        Assert.Same(note, notes.Get(note.Id));
        Assert.Null(note.DeletedZone);
    }

    [Fact]
    public void OldMemos_MoveIntoLibrary_AndZoneShowsTheFirst() => StaTest.Run(() =>
    {
        const string ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var old = new ZoneData
        {
            Mode = "memo",
            Memos = new()
            {
                new MemoData { Xaml = $"<Section xmlns=\"{ns}\"><Paragraph>첫 메모</Paragraph></Section>" },
                new MemoData { Xaml = "" },
                new MemoData { Xaml = $"<Section xmlns=\"{ns}\"><Paragraph>둘째 메모</Paragraph></Section>" },
            },
        };
        var notes = new NoteLibrary();

        var migrated = ZoneManager.MigrateMemos(old, notes);

        Assert.Null(migrated.Memos);
        Assert.Equal("첫 메모", notes.Get(migrated.NoteId)!.Title);
        Assert.Equal(new[] { "둘째 메모", "첫 메모" }.OrderBy(x => x), notes.Notes.Select(n => n.Title).OrderBy(x => x));
    });
}
