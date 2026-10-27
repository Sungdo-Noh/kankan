using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static KanKan.Native.NativeMethods;
using Screen = System.Windows.Forms.Screen;

namespace KanKan.Desktop;

/// <summary>영역 옆에 띄우는 작은 창(노트 목록, 텍스트 설정)의 위치.</summary>
internal static class PopupPlacement
{
    /// <summary>
    /// 영역 오른쪽(자리가 없으면 왼쪽)에, 화면 작업 영역 안으로 붙인다. 좌표는 물리 픽셀.
    /// 창 가장자리의 그림자 여백(shadowDip)만큼 영역과 겹쳐 놓아 바로 옆에 붙어 보이게 한다.
    /// </summary>
    public static void PlaceBeside(Window window, PixelRect zone, double shadowDip = 10)
    {
        var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        int width = (int)(window.ActualWidth * scale);
        int height = (int)(window.ActualHeight * scale);
        int overlap = (int)((shadowDip - 4) * scale);
        var area = Screen.FromRectangle(new System.Drawing.Rectangle(zone.X, zone.Y, zone.Width, zone.Height)).WorkingArea;

        int x = zone.Right - overlap;
        if (x + width > area.Right) x = zone.X - width + overlap;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(zone.Y - (int)(shadowDip * scale), area.Top, Math.Max(area.Top, area.Bottom - height));

        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }
}
