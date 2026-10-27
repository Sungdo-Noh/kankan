using KanKan.Native;
using static KanKan.Native.NativeMethods;

namespace KanKan.Desktop;

/// <summary>
/// 바탕화면을 구성하는 탐색기 창(Progman / SHELLDLL_DefView / WorkerW)을 찾아
/// 우리 창을 아이콘 뒤 또는 아이콘 위에 붙인다.
/// 바탕화면의 자식 창은 다른 앱 창 아래에 깔리고, Win+D에도 사라지지 않는다.
/// </summary>
/// <remarks>
/// Windows 11 24H2 이후: Progman ⊃ { SHELLDLL_DefView ⊃ SysListView32(아이콘), WorkerW }.
/// Progman이 WS_EX_NOREDIRECTIONBITMAP이라 그 자식 중에서는 창 전체 알파(LWA) 레이어드 창만 화면에 합성된다.
/// (일반 창이나 WPF의 픽셀 단위 투명 창(UpdateLayeredWindow)은 붙여도 보이지 않는다 — 실험으로 확인)
/// </remarks>
internal static class DesktopHost
{
    /// <summary>Progman에 보내면 배경화면용 WorkerW를 만들어 주는 비공개 메시지.</summary>
    private const uint WM_SPAWN_WORKER = 0x052C;

    public static IntPtr Progman { get; private set; }

    /// <summary>아이콘 목록(SysListView32)을 담고 있는 창. 여기의 자식은 아이콘 위에 그려진다.</summary>
    public static IntPtr DefView { get; private set; }

    /// <summary>
    /// 아이콘 뒤 창을 붙일 부모. 24H2 이후는 Progman(DefView 바로 아래 Z-순서),
    /// 이전 버전은 아이콘 창 뒤의 최상위 WorkerW.
    /// </summary>
    public static IntPtr BehindIconsParent { get; private set; }

    public static bool Initialize()
    {
        Progman = FindWindow("Progman", null);
        if (Progman == IntPtr.Zero) return false;

        SendMessageTimeout(Progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);

        DefView = FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (DefView != IntPtr.Zero)
        {
            BehindIconsParent = Progman;
        }
        else
        {
            // 이전 버전: DefView는 최상위 WorkerW 안에 있고, 배경용 WorkerW는 Z-순서상 그 다음 최상위 창
            EnumWindows((hWnd, _) =>
            {
                var defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero) return true;
                DefView = defView;
                BehindIconsParent = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                return false;
            }, IntPtr.Zero);
        }

        return DefView != IntPtr.Zero && BehindIconsParent != IntPtr.Zero;
    }

    /// <summary>아이콘 뒤에 깔리는 단색 채움 창을 만든다.</summary>
    public static FillWindow CreateFillBehindIcons(System.Windows.Media.Color color, byte alpha)
    {
        var fill = new FillWindow(BehindIconsParent, color, alpha);
        if (BehindIconsParent == Progman)
            SetWindowPos(fill.Handle, DefView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        return fill;
    }

    /// <summary>창을 아이콘 위에 붙인다. 나중에 붙인 창이 위로 올라온다.</summary>
    public static void AttachAboveIcons(IntPtr hWnd)
    {
        var style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
        style = (style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS;
        SetWindowLongPtr(hWnd, GWL_STYLE, new IntPtr(style));

        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));

        SetParent(hWnd, DefView);
    }

    /// <summary>화면 좌표(물리 픽셀)의 사각형에 맞춰 자식 창을 배치한다. Z-순서는 건드리지 않는다.</summary>
    public static void Place(IntPtr hWnd, PixelRect screenRect)
    {
        var pt = new POINT { X = screenRect.X, Y = screenRect.Y };
        MapWindowPoints(IntPtr.Zero, GetParent(hWnd), ref pt, 1);
        SetWindowPos(hWnd, IntPtr.Zero, pt.X, pt.Y, screenRect.Width, screenRect.Height,
            SWP_NOACTIVATE | SWP_NOZORDER);
    }

    public static string Describe() =>
        $"Progman=0x{Progman:X} DefView=0x{DefView:X} BehindIconsParent=0x{BehindIconsParent:X}";
}
