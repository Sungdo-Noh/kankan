namespace KanKan.Desktop;

/// <summary>화면 물리 픽셀 기준 사각형.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
