using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KanKan.Storage;

namespace KanKan.Memos;

/// <summary>
/// 메모에 붙여넣은 사진. 원본 크기·용량과 상관없이 가로 10cm로 맞춘 JPEG로 바꿔 images 폴더에 따로 저장한다.
/// 메모 저장 형식(XAML)은 사진을 담지 못하고 그 자리를 공백 하나로 남기므로,
/// 사진을 감싼 InlineUIContainer의 언어 태그(xml:lang)에 사진 id를 적어 둔다 → 저장하면 그 태그를 단 공백 Run이 되고,
/// 불러올 때 그 Run을 다시 사진으로 바꾼다. 메모끼리 복사(XamlPackage)할 때도 태그가 그대로 따라간다.
/// </summary>
internal static class MemoImages
{
    /// <summary>화면에 보이는 가로 폭: 10cm (WPF 단위는 1/96인치).</summary>
    public const double DisplayWidth = 10 / 2.54 * 96;

    /// <summary>저장하는 가로 픽셀 수. 화면 배율 200%까지 흐려 보이지 않게 보이는 폭의 2배로.</summary>
    public const int PixelWidth = 756;

    private const int JpegQuality = 85;

    private const string MarkerPrefix = "x-kkimg-";

    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };

    /// <summary>사진 파일을 두는 곳. 테스트에서 바꾼다.</summary>
    internal static string Directory { get; set; } = Path.Combine(ZoneStore.Directory, "images");

    /// <summary>클립보드의 사진들. 사진 자체(스크린샷·브라우저에서 이미지 복사)나 탐색기에서 복사한 사진 파일.</summary>
    public static List<BitmapSource> FromData(IDataObject data)
    {
        var images = new List<BitmapSource>();
        try
        {
            // 브라우저·캡처 도구는 투명도까지 담긴 PNG를 같이 넣어 준다
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
                images.Add(BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
                images.Add(bitmap);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            Log.Write($"붙여넣은 사진 읽기 실패: {ex.Message}");
        }

        if (images.Count == 0 && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    images.Add(Upright(BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
                                               or System.Runtime.InteropServices.COMException)
                {
                    Log.Write($"사진 파일 읽기 실패 {file}: {ex.Message}");
                }
            }
        }
        return images;
    }

    /// <summary>사진들을 커서 자리에 넣는다. 되돌리기 한 번에 함께 취소된다.</summary>
    public static void Insert(RichTextBox editor, IEnumerable<BitmapSource> images)
    {
        editor.BeginChange();
        try
        {
            if (!editor.Selection.IsEmpty) editor.Selection.Text = "";
            foreach (var source in images)
            {
                var jpeg = ToJpeg(source);
                var id = Guid.NewGuid().ToString("N");
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllBytes(FilePath(id), jpeg);

                var container = Container(id, Decode(jpeg), editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward));
                editor.CaretPosition = container.ElementEnd;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"사진 저장 실패: {ex.Message}");
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// 가로 <see cref="PixelWidth"/>픽셀로 늘이거나 줄이고(비율 유지, 잘라내지 않음) 투명한 곳은 흰색으로 채워 JPEG로.
    /// </summary>
    public static byte[] ToJpeg(BitmapSource source)
    {
        int height = Math.Max(1, (int)Math.Round(source.PixelHeight * (double)PixelWidth / source.PixelWidth));
        var rect = new Rect(0, 0, PixelWidth, height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, rect);
            dc.DrawImage(source, rect);
        }
        var resized = new RenderTargetBitmap(PixelWidth, height, 96, 96, PixelFormats.Pbgra32);
        resized.Render(visual);

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(resized));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>불러온 메모에서 사진 자리(사진 id 태그를 단 공백 Run)를 사진으로 바꾼다.</summary>
    public static void Restore(FlowDocument document)
    {
        var markers = new List<Run>();
        for (var p = document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                p.GetAdjacentElement(LogicalDirection.Forward) is Run run && IdOf(run) != null)
                markers.Add(run);
        }

        foreach (var run in markers)
        {
            var id = IdOf(run)!;
            BitmapSource? source = null;
            if (run.Text == " ")
            {
                try { source = Decode(File.ReadAllBytes(FilePath(id))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
                {
                    Log.Write($"사진 불러오기 실패 {id}: {ex.Message}");
                }
            }

            // 사진 파일이 없으면 자리는 그대로 둔다 (파일을 되살리면 다시 보인다).
            // 글자가 든 Run이 태그를 물려받았으면 글자로 되돌린다
            if (source == null)
            {
                if (run.Text != " ") run.ClearValue(FrameworkContentElement.LanguageProperty);
                continue;
            }
            run.SiblingInlines.InsertAfter(run, Container(id, source));
            run.SiblingInlines.Remove(run);
        }
    }

    /// <summary>어느 노트에도 쓰이지 않는 사진 파일을 지운다.</summary>
    public static void DeleteUnused(IEnumerable<string> xamls)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            var used = string.Join("\n", xamls);
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.jpg"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!used.Contains(Marker(id), StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"안 쓰는 사진 정리 실패: {ex.Message}");
        }
    }

    /// <summary>사진을 담은 InlineUIContainer. position을 주면 그 자리에 넣는다.</summary>
    private static InlineUIContainer Container(string id, BitmapSource source, TextPointer? position = null)
    {
        var image = new Image { Source = source, Width = DisplayWidth, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var container = position == null ? new InlineUIContainer(image) : new InlineUIContainer(image, position);
        container.Language = XmlLanguage.GetLanguage(Marker(id));
        return container;
    }

    /// <summary>사진 id(32자리 16진수)를 언어 태그로. 태그의 각 부분은 8자까지라 8자씩 나눈다.</summary>
    private static string Marker(string id) =>
        MarkerPrefix + string.Join("-", Enumerable.Range(0, 4).Select(i => id.Substring(i * 8, 8)));

    private static string? IdOf(TextElement element)
    {
        var tag = element.Language.IetfLanguageTag;
        return tag.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase)
            ? tag[MarkerPrefix.Length..].Replace("-", "").ToLowerInvariant()
            : null;
    }

    private static string FilePath(string id) => Path.Combine(Directory, id + ".jpg");

    private static BitmapSource Decode(byte[] jpeg)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(jpeg);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>휴대폰 사진은 돌려 찍은 방향을 EXIF에만 적어 두므로, 그 방향대로 세운다.</summary>
    private static BitmapSource Upright(BitmapFrame frame)
    {
        ushort orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value)
                orientation = value;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) { }

        double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        return angle == 0 ? frame : new TransformedBitmap(frame, new RotateTransform(angle));
    }
}
