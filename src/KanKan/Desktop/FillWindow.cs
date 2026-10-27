using System.Runtime.InteropServices;
using System.Windows.Media;
using static KanKan.Native.NativeMethods;

namespace KanKan.Desktop;

/// <summary>
/// 단색으로 칠해지는 순수 Win32 자식 창. 창 전체 알파(LWA)로 반투명하게 만든다.
/// WPF 창은 레이어드 스타일을 스스로 관리해서 바깥에서 붙인 WS_EX_LAYERED가 지워지므로 직접 만든다.
/// </summary>
internal sealed class FillWindow : IDisposable
{
    private const string ClassName = "KanKan.Fill";
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    private static readonly WndProc s_wndProc = WindowProc;
    private static bool s_registered;

    private IntPtr _brush;
    private (int Width, int Height, int Radius) _shape;

    public IntPtr Handle { get; }

    public FillWindow(IntPtr parent, Color color, byte alpha)
    {
        EnsureClass();
        _brush = CreateSolidBrush(ToColorRef(color));

        Handle = CreateWindowEx(
            (uint)(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW),
            ClassName, null,
            (uint)(WS_CHILD | WS_CLIPSIBLINGS | WS_VISIBLE | WS_DISABLED),
            0, 0, 0, 0, parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        StoreBrush();
        SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    public void SetColor(Color color)
    {
        var old = _brush;
        _brush = CreateSolidBrush(ToColorRef(color));
        StoreBrush();
        InvalidateRect(Handle, IntPtr.Zero, true);
        DeleteObject(old);
    }

    public void SetAlpha(byte alpha) => SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);

    /// <summary>창 모양을 둥근 사각형으로 자른다. 크기가 바뀔 때만 다시 만든다.</summary>
    public void SetRoundedShape(int width, int height, int radius)
    {
        if (_shape == (width, height, radius)) return;
        _shape = (width, height, radius);
        // 영역 핸들의 소유권은 SetWindowRgn 이후 시스템으로 넘어간다
        SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2), true);
    }

    public void Dispose()
    {
        if (IsWindow(Handle)) DestroyWindow(Handle);
        if (_brush != IntPtr.Zero) DeleteObject(_brush);
        _brush = IntPtr.Zero;
    }

    private void StoreBrush() => SetPropW(Handle, "KanKan.Brush", _brush);

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                GetClientRect(hWnd, out var rc);
                FillRect(wParam, ref rc, GetPropW(hWnd, "KanKan.Brush"));
                return new IntPtr(1);
            case WM_NCHITTEST:
                return new IntPtr(HTTRANSPARENT);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void EnsureClass()
    {
        if (s_registered) return;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc);
        s_registered = true;
    }

    private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));
}
