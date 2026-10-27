using System.Drawing;
using KanKan.Zones;

namespace KanKan.Tests;

public class ZonePlacementTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 2560, 1040);
    private const int Gap = 12;

    [Fact]
    public void PlacesToTheRight_WhenThereIsRoom()
    {
        var source = new Rectangle(100, 100, 400, 300);
        var spot = ZoneManager.FindSpotBeside(source, WorkArea, new[] { source }, Gap);
        Assert.Equal(new Rectangle(512, 100, 400, 300), spot);
    }

    [Fact]
    public void PlacesBelow_WhenRightIsOffScreen()
    {
        var source = new Rectangle(2100, 100, 400, 300);
        var spot = ZoneManager.FindSpotBeside(source, WorkArea, new[] { source }, Gap);
        Assert.Equal(new Rectangle(2100, 412, 400, 300), spot);
    }

    [Fact]
    public void SkipsSpotsTakenByOtherZones()
    {
        var source = new Rectangle(100, 100, 400, 300);
        var right = new Rectangle(512, 100, 400, 300);
        var spot = ZoneManager.FindSpotBeside(source, WorkArea, new[] { source, right }, Gap);
        Assert.Equal(new Rectangle(100, 412, 400, 300), spot); // 오른쪽이 차 있으니 아래
    }

    [Fact]
    public void FallsBackToOffsetInsideWorkArea_WhenNoFreeSpot()
    {
        var source = new Rectangle(0, 0, 2560, 1040); // 화면을 꽉 채운 영역
        var spot = ZoneManager.FindSpotBeside(source, WorkArea, new[] { source }, Gap);
        Assert.True(WorkArea.Contains(spot.Location));
        Assert.Equal(source.Size, spot.Size);
    }

    [Fact]
    public void NewZone_InheritsLastDeletedPositionSizeColorAndOpacity()
    {
        var deleted = new KanKan.Storage.ZoneData
        {
            Title = "지운 영역", Color = "#F5A623", Opacity = 0.25,
            X = 2000, Y = 300, Width = 450, Height = 320, Mode = "memo",
        };

        var created = ZoneManager.InheritFrom(deleted, "새 영역", Array.Empty<Rectangle>(), WorkArea, Gap);

        Assert.Equal("새 영역", created.Title);
        Assert.Equal("#F5A623", created.Color);
        Assert.Equal(0.25, created.Opacity);
        Assert.Equal((2000, 300, 450, 320), (created.X, created.Y, created.Width, created.Height));
        Assert.Equal("zone", created.Mode);
        Assert.Null(created.NoteId);
    }

    [Fact]
    public void NewZone_MovesBeside_WhenLastDeletedSpotIsTaken()
    {
        var deleted = new KanKan.Storage.ZoneData { Color = "#F5A623", X = 100, Y = 100, Width = 400, Height = 300 };
        var occupying = new Rectangle(150, 150, 400, 300);

        var created = ZoneManager.InheritFrom(deleted, "새 영역", new[] { occupying }, WorkArea, Gap);

        var rect = new Rectangle(created.X, created.Y, created.Width, created.Height);
        Assert.False(rect.IntersectsWith(occupying));
        Assert.Equal(new Size(400, 300), rect.Size);
        Assert.Equal("#F5A623", created.Color);
    }

    [Fact]
    public void NextColor_CyclesThroughPalette_AndHandlesCustomColors()
    {
        var palette = ZonePalette.Colors;
        Assert.Equal("#50B37A", ZoneManager.NextColor(palette[0].Color)); // 파랑 → 초록
        Assert.Equal("#4A90E2", ZoneManager.NextColor(palette[^1].Color)); // 마지막 → 처음
        Assert.Equal("#4A90E2", ZoneManager.NextColor(System.Windows.Media.Color.FromRgb(1, 2, 3)));
    }
}
