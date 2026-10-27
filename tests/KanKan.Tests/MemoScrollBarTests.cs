using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Tests;

public class MemoScrollBarTests
{
    [Fact]
    public void ScrollBar_BecomesNormal_OnlyAfterHoveringOneSecond() => WithMemoScrollBar((bar, thin) =>
    {
        Assert.Same(thin, bar.Style);

        Hover(bar, Mouse.MouseEnterEvent);
        Assert.Same(thin, bar.Style); // 올리자마자는 아직 얇은 막대

        Wait(TimeSpan.FromMilliseconds(1200));
        Assert.Null(bar.Style); // 1초 뒤 기본(일반) 스크롤바

        Hover(bar, Mouse.MouseLeaveEvent);
        Assert.Null(bar.Style); // 떠나도 5초 동안은 유지
    });

    [Fact]
    public void ScrollBar_StaysThin_WhenMouseLeavesWithinOneSecond() => WithMemoScrollBar((bar, thin) =>
    {
        Hover(bar, Mouse.MouseEnterEvent);
        Wait(TimeSpan.FromMilliseconds(400));
        Hover(bar, Mouse.MouseLeaveEvent);

        Wait(TimeSpan.FromMilliseconds(1000));
        Assert.Same(thin, bar.Style);
    });

    private static void WithMemoScrollBar(Action<ScrollBar, Style> test) => StaTest.Run(() =>
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Mode = "memo" }, new KanKan.Notes.NoteLibrary());
        var chrome = new ZoneChromeWindow(zone);
        try
        {
            var scroll = (ScrollViewer)chrome.FindName("MemoScroll");
            var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
            test(bar, (Style)chrome.FindResource("ThinScrollBar"));
        }
        finally
        {
            chrome.Close();
        }
    });

    private static void Hover(UIElement element, RoutedEvent routedEvent) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });

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
