using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KanKan.Memos;

namespace KanKan.Tests;

public sealed class MemoImagesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kankan-images-" + Guid.NewGuid().ToString("N"));
    private readonly string _original = MemoImages.Directory;

    public MemoImagesTests()
    {
        Log.Enabled = false;
        MemoImages.Directory = _directory;
    }

    public void Dispose()
    {
        MemoImages.Directory = _original;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(4000, 3000)] // 큰 사진은 줄이고
    [InlineData(100, 50)]    // 작은 사진은 늘린다
    public void ToJpeg_AlwaysMakesSameWidth_KeepingAspectRatio(int width, int height) => StaTest.Run(() =>
    {
        var jpeg = MemoImages.ToJpeg(Solid(width, height, Colors.SteelBlue));
        var decoded = BitmapFrame.Create(new MemoryStream(jpeg));

        Assert.Equal(MemoImages.PixelWidth, decoded.PixelWidth);
        Assert.Equal((int)Math.Round(MemoImages.PixelWidth * (double)height / width), decoded.PixelHeight);
        Assert.True(jpeg.Length < 100_000, $"{jpeg.Length} bytes");
    });

    [Fact]
    public void ToJpeg_FillsTransparencyWithWhite() => StaTest.Run(() =>
    {
        var jpeg = MemoImages.ToJpeg(Solid(50, 50, Colors.Transparent));
        var pixel = new byte[4];
        new FormatConvertedBitmap(BitmapFrame.Create(new MemoryStream(jpeg)), PixelFormats.Bgra32, null, 0)
            .CopyPixels(new Int32Rect(10, 10, 1, 1), pixel, 4, 0);

        Assert.All(pixel.Take(3), b => Assert.True(b > 245));
    });

    [Fact]
    public void PastedImage_IsShownTenCentimetersWide_AndSurvivesSaveAndLoad() => StaTest.Run(() =>
    {
        var editor = new RichTextBox();
        MemoImages.Insert(editor, new[] { Solid(1200, 800, Colors.Orange) });
        Assert.Equal(MemoImages.DisplayWidth, Assert.Single(Images(editor.Document)).Width);
        Assert.Single(Directory.GetFiles(_directory, "*.jpg"));

        var xaml = MemoFormatting.Save(editor.Document);
        var reloaded = new RichTextBox();
        MemoFormatting.Load(reloaded.Document, xaml);
        MemoImages.Restore(reloaded.Document);

        var image = Assert.Single(Images(reloaded.Document));
        Assert.Equal(MemoImages.DisplayWidth, image.Width);
        Assert.Equal(MemoImages.PixelWidth, ((BitmapSource)image.Source).PixelWidth);
        Assert.False(MemoFormatting.IsEmpty(reloaded.Document));
    });

    [Fact]
    public void PasteImageOnly_InsertsImageInsteadOfDefaultPaste() => StaTest.Run(() =>
    {
        var data = new DataObject();
        data.SetImage(Solid(300, 200, Colors.Green));
        var editor = new RichTextBox();
        var e = new DataObjectPastingEventArgs(data, isDragDrop: false, DataFormats.Bitmap);

        MemoPaste.OnPasting(editor, e);

        Assert.True(e.CommandCancelled);
        Assert.Single(Images(editor.Document));
    });

    [Fact]
    public void DeleteUnused_RemovesOnlyImagesNoNoteUses() => StaTest.Run(() =>
    {
        var editor = new RichTextBox();
        MemoImages.Insert(editor, new[] { Solid(10, 10, Colors.Red) });
        var kept = MemoFormatting.Save(editor.Document);
        MemoImages.Insert(new RichTextBox(), new[] { Solid(10, 10, Colors.Blue) });
        Assert.Equal(2, Directory.GetFiles(_directory).Length);

        MemoImages.DeleteUnused(new[] { kept });

        var left = Assert.Single(Directory.GetFiles(_directory));
        var id = Path.GetFileNameWithoutExtension(left);
        Assert.Contains(id[..8], kept);
    });

    private static BitmapSource Solid(int width, int height, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static List<Image> Images(FlowDocument document)
    {
        var images = new List<Image>();
        for (var p = document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement &&
                p.GetAdjacentElement(LogicalDirection.Forward) is Image image)
                images.Add(image);
        return images;
    }
}
