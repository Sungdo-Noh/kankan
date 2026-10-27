using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KanKan.Storage;

public sealed record ZoneData
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "새 영역";
    public string Color { get; init; } = "#4A90E2";
    /// <summary>배경 진하기 0~1.</summary>
    public double Opacity { get; init; } = 0.38;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Locked { get; init; }
    /// <summary>메모 영역을 바탕화면에서 떼어 보통 창으로 띄웠는지 (핀 3단계).</summary>
    public bool Floating { get; init; }
    /// <summary>"zone"(색 구역) 또는 "memo"(스티커 메모).</summary>
    public string Mode { get; init; } = "zone";

    /// <summary>이 영역이 메모로 보여주는 노트 (노트 보관함의 Id).</summary>
    public Guid? NoteId { get; init; }

    /// <summary>메모 글자 크기(DIP).</summary>
    public double MemoFontSize { get; init; } = 14;

    /// <summary>메모 행간 (글자 크기의 배수).</summary>
    public double MemoLineSpacing { get; init; } = 1.6;

    /// <summary>예전 형식(노트 보관함 이전)의 영역별 메모. 읽을 때만 쓰고, 불러오면 노트 보관함으로 옮긴다.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MemoData>? Memos { get; init; }
}

public sealed record MemoData
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Xaml { get; init; } = "";
}

/// <summary>노트 보관함의 노트 한 장.</summary>
public sealed record NoteData
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>목록에 보여줄 제목 (내용의 첫 줄).</summary>
    public string Title { get; init; } = "";
    /// <summary>서식을 포함한 내용 (WPF FlowDocument XAML).</summary>
    public string Xaml { get; init; } = "";
    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }

    /// <summary>이 노트를 보여주던 메모 영역을 지웠으면 그 영역(제목·색·위치·크기 등). 노트 목록에서 열면 그대로 되살린다.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoneData? DeletedZone { get; init; }
}

public sealed record StoreData
{
    public int Version { get; init; } = 2;
    public List<ZoneData> Zones { get; init; } = new();

    /// <summary>노트 보관함. 메모 영역을 지워도 노트는 여기 남는다.</summary>
    public List<NoteData> Notes { get; init; } = new();

    /// <summary>마지막으로 삭제한 영역. 다음에 새 영역을 만들 때 위치·크기·색·진하기를 이어받는다.</summary>
    public ZoneData? LastDeleted { get; init; }
}

/// <summary>영역 목록을 %APPDATA%\KanKan\zones.json 에 저장·복원한다.</summary>
internal static class ZoneStore
{
    public static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KanKan");

    private static readonly string FilePath = Path.Combine(Directory, "zones.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 그대로 저장
    };

    private const string BrokenBackupPattern = "zones.json.broken-*";

    private static readonly object WriteLock = new();
    private static long s_lastWritten;

    /// <summary>
    /// 저장 파일이 없으면(첫 실행) null. 파일을 읽을 수 없으면(다른 프로그램이 잠금 등) 예외를 그대로 낸다 —
    /// 빈 상태로 시작했다가 저장하면 기존 영역·노트를 덮어쓰게 되므로.
    /// </summary>
    public static StoreData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(FilePath), Options) ?? new StoreData();
            // 직접 고친 파일에 "zones": null 등이 있어도 빈 목록으로
            return data with { Zones = data.Zones ?? new(), Notes = data.Notes ?? new() };
        }
        catch (JsonException ex)
        {
            // 깨진 파일은 덮어쓰지 않고 옆에 보관한다
            var backup = FilePath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, backup);
            Log.Write($"zones.json 읽기 실패, {backup} 로 보관: {ex.Message}");
            return new StoreData();
        }
    }

    /// <summary>깨져서 옆에 보관해 둔 저장 파일들의 내용 (그 파일을 되살릴 수 있게 거기서 쓰던 사진을 지우지 않으려고).</summary>
    public static IEnumerable<string> BrokenBackups() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, BrokenBackupPattern).Select(File.ReadAllText)
            : Enumerable.Empty<string>();

    /// <summary>
    /// 저장한다. 백그라운드 스레드에서 불러도 된다.
    /// sequence는 내용을 만든 순서로, 이미 더 나중에 만든 내용을 썼으면 건너뛴다 (늦게 끝난 저장이 새 내용을 덮지 않게).
    /// </summary>
    public static void Save(StoreData data, long sequence)
    {
        var json = JsonSerializer.Serialize(data, Options);
        lock (WriteLock)
        {
            if (sequence <= s_lastWritten) return;
            System.IO.Directory.CreateDirectory(Directory);
            // 쓰는 도중 꺼져도 기존 파일이 깨지지 않도록 임시 파일에 쓴 뒤 교체
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
            s_lastWritten = sequence;
        }
    }
}
