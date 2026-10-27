using System.Drawing;
using System.Windows.Forms;
using KanKan.Startup;
using KanKan.Zones;

namespace KanKan.Tray;

/// <summary>작업 표시줄 알림 영역 아이콘과 메뉴.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ZoneManager _zones;
    private readonly ToolStripMenuItem _toggleVisible;
    private readonly ToolStripMenuItem _autoStart;

    public TrayIcon(ZoneManager zones, Action exit)
    {
        _zones = zones;

        _toggleVisible = new ToolStripMenuItem("모든 영역 숨기기", null, (_, _) => _zones.SetVisible(!_zones.IsVisible));
        _autoStart = new ToolStripMenuItem("Windows 시작 시 자동 실행", null, (_, _) => AutoStart.IsEnabled = !AutoStart.IsEnabled);

        var menu = new ContextMenuStrip();
        menu.Items.Add("새 영역 만들기", null, (_, _) => _zones.CreateZone());
        menu.Items.Add(_toggleVisible);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());
        menu.Opening += (_, _) =>
        {
            _toggleVisible.Text = _zones.IsVisible ? "모든 영역 숨기기" : "모든 영역 보이기";
            _autoStart.Checked = AutoStart.IsEnabled;
        };

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "칸칸",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _zones.CreateZone();
    }

    /// <summary>작업 표시줄 알림(토스트)을 잠깐 띄운다.</summary>
    public void Notify(string message) => _icon.ShowBalloonTip(4000, "칸칸", message, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>내장한 앱 아이콘에서 현재 DPI의 작은 아이콘 크기를 고른다.</summary>
    private static Icon CreateIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("KanKan.ico")!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
