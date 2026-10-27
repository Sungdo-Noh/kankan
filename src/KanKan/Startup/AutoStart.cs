using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace KanKan.Startup;

/// <summary>
/// 작업 스케줄러의 "로그온 시" 작업으로 Windows 로그인 시 자동 실행을 켜고 끈다.
/// HKCU Run 키는 Windows가 등록된 앱을 하나씩 차례로 실행해서, 앞의 앱(카카오톡 등)이 느리면
/// 로그인 후 수십 초가 지나서야 칸칸이 뜬다. 작업 스케줄러는 로그온 즉시 따로 실행한다.
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "KanKan";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "KanKan";
    private const string TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // ITaskFolder.RegisterTask 인자
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;

    private static string ExePath => Environment.ProcessPath!;

    /// <summary>지금 실행 중인 exe 경로로 작업이 등록·활성화되어 있을 때만 true.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                var task = RootFolder().GetTask(TaskName);
                if (!(bool)task.Enabled) return false;
                var command = XDocument.Parse((string)task.Xml).Descendants(XName.Get("Command", TaskNs)).FirstOrDefault()?.Value;
                return string.Equals(command, ExePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (COMException)
            {
                return false; // 작업 없음
            }
        }
        set
        {
            try
            {
                if (value) Register();
                else Unregister();
                DeleteLegacyRunValue();
            }
            catch (COMException ex)
            {
                Log.Write($"자동 실행 {(value ? "켜기" : "끄기")} 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 예전 버전이 Run 키에 등록해 둔 자동 실행을 작업 스케줄러로 옮긴다.
    /// 다른 exe(예: 설치본이 있는데 개발 빌드를 실행)를 가리키는 등록은 건드리지 않는다.
    /// </summary>
    public static void MigrateFromRunKey()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey))
        {
            if (key?.GetValue(LegacyValueName) is not string command
                || !command.Contains(ExePath, StringComparison.OrdinalIgnoreCase)) return;
        }
        try
        {
            Register();
            DeleteLegacyRunValue();
            Log.Write("자동 실행을 Run 키에서 작업 스케줄러로 옮김");
        }
        catch (COMException ex)
        {
            // 옮기지 못하면 느리더라도 Run 키로 계속 자동 실행되게 둔다
            Log.Write($"자동 실행 옮기기 실패 (Run 키 유지): {ex.Message}");
        }
    }

    private static void Register()
    {
        var user = WindowsIdentity.GetCurrent().User!.Value;
        XNamespace ns = TaskNs;
        var xml = new XDocument(
            new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "칸칸 - Windows 로그인 시 자동 실행")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", user))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", user),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    // 기본값은 배터리 사용 중이면 실행 안 함 / 72시간 뒤 강제 종료 / 낮은 우선순위라서 모두 바꾼다
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "5")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", ExePath),
                        new XElement(ns + "Arguments", App.AutoStartArg),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(ExePath)!)))));

        RootFolder().RegisterTask(TaskName, xml.ToString(), TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
    }

    private static void Unregister()
    {
        try
        {
            RootFolder().DeleteTask(TaskName, 0);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            // 이미 없음
        }
    }

    private static void DeleteLegacyRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    private static dynamic RootFolder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        service.Connect();
        return service.GetFolder(@"\");
    }
}
