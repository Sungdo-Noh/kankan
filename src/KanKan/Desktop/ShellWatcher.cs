using System.Windows.Interop;
using static KanKan.Native.NativeMethods;

namespace KanKan.Desktop;

/// <summary>
/// 숨은 최상위 창으로 시스템 알림을 받는다.
/// - 탐색기가 다시 시작되면(작업 표시줄이 새로 만들어지면) 알려준다.
///   영역 창은 탐색기 창의 자식이라 탐색기가 죽을 때 함께 파괴되므로 다시 붙여야 한다.
/// - Windows 종료·재부팅·로그아웃 직전을 알려준다.
/// </summary>
internal sealed class ShellWatcher : IDisposable
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION = 0x0016;

    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource _source;

    public ShellWatcher()
    {
        // TaskbarCreated·종료 알림은 최상위 창에만 오므로 메시지 전용 창이 아닌 숨은 팝업 창을 쓴다
        _source = new HwndSource(new HwndSourceParameters("KanKan.ShellWatcher") { WindowStyle = WS_POPUP });
        _source.AddHook(WndProc);
    }

    public event Action? ShellRestarted;

    /// <summary>Windows가 곧 종료된다. 이 안에서 저장을 끝내야 한다.</summary>
    public event Action? SessionEnding;

    public void Dispose() => _source.Dispose();

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreated) ShellRestarted?.Invoke();
        else if (msg == WM_QUERYENDSESSION) SessionEnding?.Invoke();
        else if (msg == WM_ENDSESSION && wParam != IntPtr.Zero) SessionEnding?.Invoke();

        // 종료를 막지 않는다 (WM_QUERYENDSESSION 기본 처리 = 허용)
        return IntPtr.Zero;
    }
}
