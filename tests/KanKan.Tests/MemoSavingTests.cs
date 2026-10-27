using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KanKan.Memos;
using KanKan.Notes;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Tests;

/// <summary>메모 입력을 노트에 넣는 시점 (키마다가 아니라 입력이 멈춘 뒤·노트를 바꾸기 전·저장 전).</summary>
public class MemoSavingTests
{
    public MemoSavingTests() => Log.Enabled = false;

    [Fact]
    public void Typing_IsReportedOnceAfterPause_NotOnEveryKey() => StaTest.Run(() =>
    {
        var card = new MemoCard(new NoteLibrary().Create());
        var reported = new List<string>();
        card.ContentChanged += (_, _, text) => reported.Add(text.Trim());

        foreach (var ch in "할 일") card.Editor.AppendText(ch.ToString());
        Assert.Empty(reported);

        Wait(TimeSpan.FromMilliseconds(500));
        Assert.Equal(new[] { "할 일" }, reported);

        card.Flush(); // 알릴 변경이 없으면 아무것도 안 한다
        Assert.Single(reported);
    });

    [Fact]
    public void OpeningAnotherNote_KeepsWhatWasJustTyped() => StaTest.Run(() =>
    {
        var notes = new NoteLibrary();
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Mode = "memo" }, notes);
        var first = zone.Note!;
        var chrome = new ZoneChromeWindow(zone);
        try
        {
            var card = ((StackPanel)chrome.FindName("MemoList")).Children.OfType<MemoCard>().Single();
            card.Editor.CaretPosition.InsertTextInRun("방금 친 글");

            zone.OpenNote(notes.Create());
            chrome.ApplyMode(); // Attach 없이 만든 창이라 영역이 대신 불러 주지 못한다

            Assert.Equal("방금 친 글", first.Title);
        }
        finally
        {
            chrome.Close();
        }
    });

    [Fact]
    public void NoteList_DeactivatedWhileClosing_DoesNotThrow() => StaTest.Run(() =>
    {
        var zone = new Zone(new ZoneData { X = 100, Y = 100, Width = 400, Height = 300, Mode = "memo" }, new NoteLibrary());
        NoteListWindow.ShowFor(zone);
        var window = (Window)typeof(NoteListWindow).GetField("s_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Wait(TimeSpan.FromMilliseconds(100));

        // 실제로는 닫히는 창이 포커스를 잃으며 Deactivated가 온다
        var onDeactivated = typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!;
        window.Closing += (_, _) => onDeactivated.Invoke(window, new object[] { EventArgs.Empty });

        window.Close();
    });

    [Fact]
    public void NoteList_OpensEveryNoteInNewZone_AndLeavesThisMemoAlone() => StaTest.Run(() =>
    {
        var notes = new NoteLibrary();
        var here = new Zone(new ZoneData { X = 100, Y = 100, Width = 400, Height = 300, Mode = "memo" }, notes);
        var shownHere = here.NoteId;
        var saved = notes.Create("x", "다른 노트");
        var heldByZoneMode = notes.Create("y", "영역 모드 영역이 들고 있던 노트");
        var zoneMode = new Zone(new ZoneData { Mode = "zone", NoteId = heldByZoneMode.Id }, notes);
        notes.FindOwner = id => new[] { here, zoneMode }.FirstOrDefault(z => z.NoteId == id);
        var opened = new List<(Note Note, Zone Beside)>();
        notes.OpenInNewZone = (n, z) => opened.Add((n, z));

        ClickInNoteList(here, saved);
        ClickInNoteList(here, heldByZoneMode);
        ClickInNoteList(here, here.Note!); // 이미 이 영역에 있는 노트는 아무것도 안 한다

        Assert.Equal(new[] { (saved, here), (heldByZoneMode, here) }, opened);
        Assert.Equal(shownHere, here.NoteId); // 이 영역의 메모는 그대로
        Assert.Null(zoneMode.NoteId);         // 두 영역이 한 노트를 같이 갖지 않게 넘겨준다
    });

    private static void ClickInNoteList(Zone zone, Note note)
    {
        NoteListWindow.ShowFor(zone);
        var window = typeof(NoteListWindow).GetField("s_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        typeof(NoteListWindow).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { note });
    }

    /// <summary>디스패처를 돌리면서 기다린다 (그동안 DispatcherTimer가 동작한다).</summary>
    private static void Wait(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
