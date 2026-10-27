using KanKan.Notes;
using KanKan.Storage;
using KanKan.Zones;

namespace KanKan.Tests;

public class ZoneDataTests
{
    [Fact]
    public void Zone_RoundTripsSettingsAndNote()
    {
        var notes = new NoteLibrary();
        var note = notes.Create("<Section/>", "할 일");
        var data = new ZoneData
        {
            Title = "로또타임 블로그",
            Color = "#A07855",
            Opacity = 0.15,
            X = 10, Y = 20, Width = 400, Height = 300,
            Locked = true,
            Mode = "memo",
            NoteId = note.Id,
        };

        var saved = new Zone(data, notes).ToData();

        Assert.Equal(data, saved);
    }

    [Theory]
    [InlineData("#XYZ")]
    [InlineData("")]
    [InlineData(null)]
    public void Zone_WithUnreadableColor_FallsBackToFirstPaletteColor(string? color)
    {
        var zone = new Zone(new ZoneData { Color = color!, Width = 400, Height = 300 }, new NoteLibrary());

        Assert.Equal(ZonePalette.Colors[0].Color, zone.Color);
    }

    [Fact]
    public void MemoZone_WithoutNote_GetsNewEmptyNote()
    {
        var notes = new NoteLibrary();
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Mode = "memo" }, notes);

        Assert.NotNull(zone.Note);
        Assert.True(zone.Note!.IsEmpty);
    }

    [Fact]
    public void FloatingMemo_RoundTrips()
    {
        var data = new ZoneData { Width = 400, Height = 300, Mode = "memo", Floating = true };
        var zone = new Zone(data, new NoteLibrary());

        Assert.Equal(PinState.Floating, zone.Pin);
        Assert.True(zone.ToData().Floating);
    }

    [Fact]
    public void Floating_IsOnlyForMemoMode()
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Floating = true }, new NoteLibrary());
        Assert.Equal(PinState.Free, zone.Pin);

        zone.SetPin(PinState.Floating);
        Assert.Equal(PinState.Free, zone.Pin);
    }

    [Fact]
    public void SwitchingFloatingMemoToZoneMode_Unfloats()
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300, Mode = "memo", Floating = true }, new NoteLibrary());

        zone.SetMode(ZoneMode.Zone);

        Assert.Equal(PinState.Free, zone.Pin);
        Assert.False(zone.ToData().Floating);
    }

    [Fact]
    public void OldSaveFile_WithoutModeOrNote_LoadsAsZoneMode()
    {
        var zone = new Zone(new ZoneData { Width = 400, Height = 300 }, new NoteLibrary());
        Assert.Equal(ZoneMode.Zone, zone.Mode);
        Assert.Null(zone.Note);
    }
}
