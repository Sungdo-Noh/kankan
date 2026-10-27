using System.Windows.Media;

namespace KanKan.Zones;

/// <summary>영역 색·배경 진하기 선택지.</summary>
internal static class ZonePalette
{
    public static readonly (string Name, Color Color)[] Colors =
    {
        ("파랑", Rgb(0x4A, 0x90, 0xE2)),
        ("초록", Rgb(0x50, 0xB3, 0x7A)),
        ("주황", Rgb(0xF5, 0xA6, 0x23)),
        ("보라", Rgb(0x9B, 0x6B, 0xDF)),
        ("빨강", Rgb(0xE5, 0x53, 0x4B)),
        ("회색", Rgb(0x8A, 0x94, 0xA6)),
        ("청록", Rgb(0x2B, 0xB3, 0xA6)),
        ("분홍", Rgb(0xE0, 0x6B, 0xA8)),
        ("노랑", Rgb(0xF2, 0xC9, 0x4C)),
        ("갈색", Rgb(0xA0, 0x78, 0x55)),
    };

    public const double DefaultOpacity = 0.38;

    public static readonly (string Name, double Opacity)[] Opacities =
    {
        ("아주 연하게", 0.15),
        ("연하게", 0.25),
        ("보통", DefaultOpacity),
        ("진하게", 0.55),
    };

    /// <summary>저장된 색("#RRGGBB"). 읽을 수 없는 값(직접 고친 파일 등)이면 첫 색으로 — 영역 하나 때문에 시작이 실패하지 않게.</summary>
    public static Color Parse(string? text)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(text) && ColorConverter.ConvertFromString(text) is Color color) return color;
        }
        catch (FormatException) { }
        return Colors[0].Color;
    }

    /// <summary>밝은 색 위에는 어두운 글씨를 쓴다.</summary>
    public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.68;

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
