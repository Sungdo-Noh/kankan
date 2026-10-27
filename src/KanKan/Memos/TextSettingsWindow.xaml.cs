using System.Windows;
using System.Windows.Input;
using KanKan.Desktop;
using KanKan.Zones;

namespace KanKan.Memos;

/// <summary>메모 영역의 ⋯ → 텍스트 설정. 글자 크기·행간을 슬라이더로 바꾸면 바로 메모에 반영된다.</summary>
public partial class TextSettingsWindow : Window
{
    private static TextSettingsWindow? s_current;

    private readonly Zone _zone;
    private bool _initializing = true;

    /// <summary>닫히는 중. 창이 닫힐 때도 Deactivated가 오는데, 그때 Close를 또 부르면 예외가 난다.</summary>
    private bool _closing;

    private TextSettingsWindow(Zone zone)
    {
        InitializeComponent();
        _zone = zone;
        Closing += (_, _) => _closing = true;
        FontSizeSlider.Minimum = Zone.MinMemoFontSize;
        FontSizeSlider.Maximum = Zone.MaxMemoFontSize;
        LineSpacingSlider.Minimum = Zone.MinMemoLineSpacing;
        LineSpacingSlider.Maximum = Zone.MaxMemoLineSpacing;
        FontSizeSlider.Value = zone.MemoFontSize;
        LineSpacingSlider.Value = zone.MemoLineSpacing;
        _initializing = false;
        UpdateLabels();
    }

    public static void ShowFor(Zone zone)
    {
        s_current?.Close();
        var window = new TextSettingsWindow(zone);
        s_current = window;
        window.Closed += (_, _) => { if (s_current == window) s_current = null; };
        window.Show();
        PopupPlacement.PlaceBeside(window, zone.Bounds);
        window.Activate();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _zone.SetMemoText(FontSizeSlider.Value, LineSpacingSlider.Value);
        UpdateLabels();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        FontSizeSlider.Value = Zone.DefaultMemoFontSize;
        LineSpacingSlider.Value = Zone.DefaultMemoLineSpacing;
    }

    private void UpdateLabels()
    {
        FontSizeValue.Text = $"{FontSizeSlider.Value:0}";
        LineSpacingValue.Text = $"{LineSpacingSlider.Value:0.0}배";
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing && IsLoaded) Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
