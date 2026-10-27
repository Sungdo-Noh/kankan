using System.IO;
using System.Windows;
using KanKan.Desktop;
using KanKan.Startup;
using KanKan.Storage;
using KanKan.Tray;
using KanKan.Zones;

namespace KanKan;

public partial class App : Application
{
    /// <summary>로그인 시 자동 실행(작업 스케줄러)에 붙는 인자. 이때는 조용히 시작한다.</summary>
    public const string AutoStartArg = "--autostart";

    private const string MutexName = "KanKan.SingleInstance";
    private const string ActivateEventName = "KanKan.Activate";

    /// <summary>설치·제거 스크립트가 강제 종료 대신 이 이벤트로 종료를 부탁한다 (저장할 틈을 주려고).</summary>
    private const string ExitEventName = "KanKan.Exit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private ZoneManager? _zones;
    private ShellWatcher? _shellWatcher;
    private TrayIcon? _tray;
    private bool _reattaching;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 창 하나의 오류로 앱이 죽으면 바탕화면의 영역이 모두 사라지므로, 기록만 하고 계속 돈다
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"처리되지 않은 예외: {args.Exception}");
            args.Handled = true;
        };

        _singleInstance = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // 이미 실행 중: 기존 칸칸에게 "사용자가 다시 실행했다"고 알리고 끝낸다
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, _) => Dispatcher.BeginInvoke(OnUserLaunched), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, Timeout.Infinite, executeOnlyOnce: true);

        try
        {
            AutoStart.MigrateFromRunKey();

            // 로그인 직후 자동 실행되면 바탕화면이 아직 준비되지 않았을 수 있어 잠시 기다린다
            if (!await WaitForDesktopAsync(TimeSpan.FromSeconds(60)))
            {
                Log.Write("바탕화면 창을 찾지 못해 종료");
                MessageBox.Show("바탕화면 창을 찾지 못했습니다.", "칸칸", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            Log.Write($"시작: {DesktopHost.Describe()}");

            // 다 불러온 뒤에야 _zones에 넣는다: 도중에 실패하면 종료하면서 반쯤 불러온 내용을 저장해 버리지 않게
            var zones = new ZoneManager();
            zones.Load();
            zones.AttachAll();
            _zones = zones;

            _shellWatcher = new ShellWatcher();
            _shellWatcher.ShellRestarted += OnShellRestarted;
            _shellWatcher.SessionEnding += () => _zones?.SaveNow();

            _tray = new TrayIcon(_zones, Shutdown);
        }
        catch (Exception ex)
        {
            // 위의 전역 처리로 넘기면 트레이도 영역도 없이 프로세스만 남아, 다시 실행해도 아무 일도 일어나지 않는다
            Log.Write($"시작 실패: {ex}");
            MessageBox.Show($"칸칸을 시작하지 못했습니다.\n{ex.Message}\n\n자세한 내용: {Path.Combine(ZoneStore.Directory, "kankan.log")}",
                "칸칸", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!e.Args.Contains(AutoStartArg)) OnUserLaunched();
    }

    /// <summary>
    /// 사용자가 바로가기 등으로 직접 실행했다(이미 실행 중이었어도).
    /// 영역은 다른 창 아래 바탕화면에 있어서 실행해도 눈에 안 띄므로, 보이게 만들고 알려준다.
    /// </summary>
    private void OnUserLaunched()
    {
        if (_zones == null || _tray == null) return;
        Log.Write($"사용자 실행 (영역 {_zones.Count}개, 보임={_zones.IsVisible})");

        if (!_zones.IsVisible) _zones.SetVisible(true);
        if (_zones.Count == 0)
        {
            _zones.CreateZone();
            _tray.Notify("새 영역을 하나 만들었어요. 영역은 바탕화면(Win+D)에 있어요.");
        }
        else
        {
            _tray.Notify($"실행 중이에요. 영역 {_zones.Count}개는 바탕화면(Win+D)에 있어요.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        _exitWait?.Unregister(null);
        _exitEvent?.Dispose();
        _tray?.Dispose();
        _shellWatcher?.Dispose();
        _zones?.Shutdown();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private async void OnShellRestarted()
    {
        if (_reattaching || _zones == null) return;
        _reattaching = true;
        try
        {
            Log.Write("탐색기 재시작 감지");
            if (await WaitForDesktopAsync(TimeSpan.FromSeconds(30)))
            {
                _zones.ReattachAll();
                Log.Write($"다시 붙임: {DesktopHost.Describe()}");
            }
            else
            {
                Log.Write("탐색기 재시작 후 바탕화면 창을 찾지 못함");
            }
        }
        finally
        {
            _reattaching = false;
        }
    }

    private static async Task<bool> WaitForDesktopAsync(TimeSpan timeout)
    {
        var deadline = DateTime.Now + timeout;
        while (!DesktopHost.Initialize())
        {
            if (DateTime.Now > deadline) return false;
            await Task.Delay(1000);
        }
        return true;
    }
}
