using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Views;

public partial class TemplateLayoutCanvas : UserControl
{
    private const int RenderDpi = 300;
    private const double MaxCanvasWidthPx = 380;

    private readonly List<TextBlockHandle> _handles = new();
    private TextBlockHandle? _selected;
    private TextBlockHandle? _dragging;
    private Point _dragStartCanvas;
    private double _dragStartXmm;
    private double _dragStartYmm;
    private bool _isLoading;
    private bool _isUpdatingFontBox;
    private double _pixelsPerMm = 10;

    private sealed class TextBlockHandle
    {
        public required int Index { get; init; }
        public required Border Root { get; init; }
        public required Image PreviewImage { get; init; }
        public required string SourceText { get; set; }
        public required string DisplayText { get; set; }
        public double Xmm { get; set; }
        public double Ymm { get; set; }
        public double FontSizePt { get; set; }
        public bool Bold { get; set; }
        public TextBlockLayout Layout { get; set; }
        public TextFlowDirection Flow { get; set; }
    }

    public event EventHandler<TemplateTextEdit>? TextBlocksEdited;
    public event EventHandler<TemplateTextEdit>? TextBlocksCommitted;

    public TemplateLayoutCanvas()
    {
        InitializeComponent();
        FontSmallerButton.IsEnabled = false;
        FontLargerButton.IsEnabled = false;
        SelectedFontSizeBox.IsEnabled = false;
        LayoutToggleButton.IsEnabled = false;
    }

    public void LoadLayout(
        double labelWidthMm,
        double labelHeightMm,
        double dmWidthMm,
        double dmHeightMm,
        double dmXmm,
        double dmYmm,
        IReadOnlyList<TemplateTextBlockViewItem> blocks)
    {
        _isLoading = true;
        try
        {
            _pixelsPerMm = Math.Min(12, MaxCanvasWidthPx / Math.Max(1, labelWidthMm));
            ScaleInfoText.Text =
                $"Этикетка {labelWidthMm.ToString("0.#", CultureInfo.InvariantCulture)}×{labelHeightMm.ToString("0.#", CultureInfo.InvariantCulture)} мм · " +
                $"DM {dmWidthMm.ToString("0.#", CultureInfo.InvariantCulture)}×{dmHeightMm.ToString("0.#", CultureInfo.InvariantCulture)} мм · " +
                $"1 мм ≈ {_pixelsPerMm.ToString("0.#", CultureInfo.InvariantCulture)} px";

            LayoutCanvas.Children.Clear();
            _handles.Clear();
            _selected = null;
            UpdateFontControls();

            var labelW = labelWidthMm * _pixelsPerMm;
            var labelH = labelHeightMm * _pixelsPerMm;
            LayoutCanvas.Width = labelW + 24;
            LayoutCanvas.Height = labelH + 24;

            var labelRect = new Rectangle
            {
                Width = labelW,
                Height = labelH,
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,
                Fill = Brushes.White,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(labelRect, 12);
            Canvas.SetTop(labelRect, 12);
            LayoutCanvas.Children.Add(labelRect);

            var dmRect = new Rectangle
            {
                Width = dmWidthMm * _pixelsPerMm,
                Height = dmHeightMm * _pixelsPerMm,
                Stroke = new SolidColorBrush(Color.FromRgb(0x2D, 0x6A, 0x4E)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 45, 106, 78))
            };
            Canvas.SetLeft(dmRect, 12 + dmXmm * _pixelsPerMm);
            Canvas.SetTop(dmRect, 12 + dmYmm * _pixelsPerMm);
            LayoutCanvas.Children.Add(dmRect);

            var dmLabel = new TextBlock
            {
                Text = $"DM {dmWidthMm.ToString("0.#", CultureInfo.InvariantCulture)}×{dmHeightMm.ToString("0.#", CultureInfo.InvariantCulture)}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x6A, 0x4E)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dmLabel, 12 + dmXmm * _pixelsPerMm);
            Canvas.SetTop(dmLabel, 12 + dmYmm * _pixelsPerMm - 14);
            LayoutCanvas.Children.Add(dmLabel);

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var handle = CreateHandle(i, block);
                _handles.Add(handle);
                LayoutCanvas.Children.Add(handle.Root);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private TextBlockHandle CreateHandle(int index, TemplateTextBlockViewItem block)
    {
        var display = block.PreviewText ?? block.Text;
        var (layout, flow) = TextBlockStyleHelper.GetStyle(block.Layout, block.Flow, block.Orientation);
        var (wMm, hMm) = TextBlockRenderHelper.MeasureBlockMm(
            display, block.FontSizePt, block.Bold, layout, flow, RenderDpi);
        var wPx = Math.Max(8, wMm * _pixelsPerMm);
        var hPx = Math.Max(8, hMm * _pixelsPerMm);

        var preview = new Image
        {
            Source = SnippetImageFactory.Create(display, block.FontSizePt, block.Bold, layout, flow, RenderDpi),
            Stretch = Stretch.Uniform,
            Width = wPx,
            Height = hPx,
            IsHitTestVisible = false
        };

        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 0, 120, 200)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7C, 0xB8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(1),
            Width = wPx + 2,
            Height = hPx + 2,
            Cursor = Cursors.SizeAll,
            Child = preview,
            Tag = index
        };

        root.MouseLeftButtonDown += OnHandleMouseDown;
        root.MouseMove += OnHandleMouseMove;
        root.MouseLeftButtonUp += OnHandleMouseUp;

        Canvas.SetLeft(root, 12 + block.Xmm * _pixelsPerMm);
        Canvas.SetTop(root, 12 + block.Ymm * _pixelsPerMm);

        return new TextBlockHandle
        {
            Index = index,
            Root = root,
            PreviewImage = preview,
            SourceText = block.Text,
            DisplayText = display,
            Xmm = block.Xmm,
            Ymm = block.Ymm,
            FontSizePt = block.FontSizePt,
            Bold = block.Bold,
            Layout = layout,
            Flow = flow
        };
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int index)
            return;

        _selected = _handles.FirstOrDefault(h => h.Index == index);
        if (_selected == null)
            return;

        SelectHandle(_selected);
        _dragging = _selected;
        _dragStartCanvas = e.GetPosition(LayoutCanvas);
        _dragStartXmm = _selected.Xmm;
        _dragStartYmm = _selected.Ymm;
        _selected.Root.CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(LayoutCanvas);
        var dx = (pos.X - _dragStartCanvas.X) / _pixelsPerMm;
        var dy = (pos.Y - _dragStartCanvas.Y) / _pixelsPerMm;
        _dragging.Xmm = RoundMm(Math.Max(0, _dragStartXmm + dx));
        _dragging.Ymm = RoundMm(Math.Max(0, _dragStartYmm + dy));
        Canvas.SetLeft(_dragging.Root, 12 + _dragging.Xmm * _pixelsPerMm);
        Canvas.SetTop(_dragging.Root, 12 + _dragging.Ymm * _pixelsPerMm);
    }

    private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == null)
            return;

        _dragging.Root.ReleaseMouseCapture();
        _dragging = null;
        RaiseCommitted();
        e.Handled = true;
    }

    private void SelectHandle(TextBlockHandle handle)
    {
        foreach (var h in _handles)
        {
            h.Root.BorderBrush = ReferenceEquals(h, handle)
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(0x3A, 0x7C, 0xB8));
            h.Root.BorderThickness = ReferenceEquals(h, handle) ? new Thickness(2) : new Thickness(1);
        }

        _selected = handle;
        UpdateFontControls();
    }

    private void UpdateFontControls()
    {
        var enabled = _selected != null;
        FontSmallerButton.IsEnabled = enabled;
        FontLargerButton.IsEnabled = enabled;
        SelectedFontSizeBox.IsEnabled = enabled;
        LayoutToggleButton.IsEnabled = enabled;
        DirRightButton.IsEnabled = enabled;
        DirLeftButton.IsEnabled = enabled;
        DirDownButton.IsEnabled = enabled;
        DirUpButton.IsEnabled = enabled;

        var vertical = enabled && _selected!.Layout == TextBlockLayout.Vertical;

        _isUpdatingFontBox = true;
        try
        {
            SelectedFontSizeBox.Text = enabled
                ? _selected!.FontSizePt.ToString("0.#", CultureInfo.InvariantCulture)
                : "—";
            LayoutToggleButton.Content = vertical ? "Верт." : "Гориз.";
        }
        finally
        {
            _isUpdatingFontBox = false;
        }

        HighlightDirectionButton(DirRightButton, enabled && _selected!.Flow == TextFlowDirection.Right);
        HighlightDirectionButton(DirLeftButton, enabled && _selected!.Flow == TextFlowDirection.Left);
        HighlightDirectionButton(DirDownButton, enabled && _selected!.Flow == TextFlowDirection.Down);
        HighlightDirectionButton(DirUpButton, enabled && _selected!.Flow == TextFlowDirection.Up);
    }

    private void HighlightDirectionButton(Button button, bool active)
    {
        button.Background = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("CardBrush");
        button.Foreground = active
            ? Brushes.White
            : (Brush)FindResource("TextBrush");
    }

    private void OnLayoutToggleClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        _selected.Layout = TextBlockStyleHelper.ToggleLayout(_selected.Layout, _selected.Flow);
        if (_selected.Layout == TextBlockLayout.Vertical
            && _selected.Flow is TextFlowDirection.Right or TextFlowDirection.Left)
        {
            _selected.Flow = TextBlockStyleHelper.DefaultFlowForLayout(_selected.Layout);
        }
        else if (_selected.Layout == TextBlockLayout.Horizontal
                 && _selected.Flow is TextFlowDirection.Up or TextFlowDirection.Down)
        {
            _selected.Flow = TextBlockStyleHelper.DefaultFlowForLayout(_selected.Layout);
        }
        RefreshHandleVisual(_selected);
        UpdateFontControls();
        RaiseEdited();
    }

    private void OnDirRightClick(object sender, RoutedEventArgs e) => SetFlow(TextFlowDirection.Right);
    private void OnDirLeftClick(object sender, RoutedEventArgs e) => SetFlow(TextFlowDirection.Left);
    private void OnDirDownClick(object sender, RoutedEventArgs e) => SetFlow(TextFlowDirection.Down);
    private void OnDirUpClick(object sender, RoutedEventArgs e) => SetFlow(TextFlowDirection.Up);

    private void SetFlow(TextFlowDirection flow)
    {
        if (_selected == null || _selected.Flow == flow)
            return;

        _selected.Flow = flow;
        RefreshHandleVisual(_selected);
        UpdateFontControls();
        RaiseEdited();
    }

    private void OnFontSmallerClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        _selected.FontSizePt = Math.Clamp(RoundMm(_selected.FontSizePt - 0.5), 2, 12);
        RefreshHandleVisual(_selected);
        UpdateFontControls();
        RaiseEdited();
    }

    private void OnFontLargerClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        _selected.FontSizePt = Math.Clamp(RoundMm(_selected.FontSizePt + 0.5), 2, 12);
        RefreshHandleVisual(_selected);
        UpdateFontControls();
        RaiseEdited();
    }

    private void OnFontSizeBoxLostFocus(object sender, RoutedEventArgs e) => ApplyFontSizeFromBox();

    private void OnFontSizeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFontSizeFromBox();
            e.Handled = true;
        }
    }

    private void ApplyFontSizeFromBox()
    {
        if (_isUpdatingFontBox || _selected == null)
            return;

        var text = SelectedFontSizeBox.Text.Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
        {
            UpdateFontControls();
            return;
        }

        pt = Math.Clamp(RoundMm(pt), 2, 12);
        if (Math.Abs(pt - _selected.FontSizePt) < 0.05)
        {
            UpdateFontControls();
            return;
        }

        _selected.FontSizePt = pt;
        RefreshHandleVisual(_selected);
        UpdateFontControls();
        RaiseEdited();
    }

    private void RefreshHandleVisual(TextBlockHandle handle)
    {
        var (wMm, hMm) = TextBlockRenderHelper.MeasureBlockMm(
            handle.DisplayText, handle.FontSizePt, handle.Bold, handle.Layout, handle.Flow, RenderDpi);
        var wPx = Math.Max(8, wMm * _pixelsPerMm);
        var hPx = Math.Max(8, hMm * _pixelsPerMm);
        handle.PreviewImage.Source = SnippetImageFactory.Create(
            handle.DisplayText, handle.FontSizePt, handle.Bold, handle.Layout, handle.Flow, RenderDpi);
        handle.PreviewImage.Width = wPx;
        handle.PreviewImage.Height = hPx;
        handle.Root.Width = wPx + 2;
        handle.Root.Height = hPx + 2;
    }

    private TemplateTextEdit BuildEdit() =>
        new(_handles.OrderBy(h => h.Index).Select(h => new TemplateTextBlockEdit(
            h.SourceText,
            h.Xmm,
            h.Ymm,
            h.FontSizePt,
                h.Bold,
                h.Layout,
                h.Flow)).ToList());

    private void RaiseEdited()
    {
        if (_isLoading)
            return;

        TextBlocksEdited?.Invoke(this, BuildEdit());
    }

    private void RaiseCommitted()
    {
        if (_isLoading)
            return;

        TextBlocksCommitted?.Invoke(this, BuildEdit());
    }

    private static double RoundMm(double value) => Math.Round(value, 1);
}
