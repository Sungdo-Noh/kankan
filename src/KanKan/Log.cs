using System.IO;
using KanKan.Storage;

namespace KanKan;

internal static class Log
{
    private static readonly string FilePath = Path.Combine(ZoneStore.Directory, "kankan.log");

    /// <summary>단위 테스트가 실제 로그 파일을 더럽히지 않도록 끌 수 있다.</summary>
    internal static bool Enabled { get; set; } = true;

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(ZoneStore.Directory);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        // 로그는 catch 블록 안에서도 부르므로 여기서 새 예외가 나가면 안 된다
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
