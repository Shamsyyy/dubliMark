using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DoubleMark.Desktop.Views;

public partial class TemplatesView : UserControl
{
    public event RoutedEventHandler? ManageTemplatesRequested;
    public event EventHandler<string>? TemplateSelected;

    public TemplatesView() => InitializeComponent();

    public void UpdateState(TemplateViewState state)
    {
        TemplatesListPanel.Children.Clear();

        var active = state.Templates.FirstOrDefault(t => t.IsActive) ?? state.Templates.FirstOrDefault();
        foreach (var template in state.Templates)
            TemplatesListPanel.Children.Add(BuildTemplateRow(template));

        if (active == null)
        {
            TemplateNameText.Text = "Нет шаблонов";
            TemplateLabelSizeText.Text = "—";
            TemplateDmSizeText.Text = "—";
            TemplatePositionText.Text = "—";
            TemplateBlocksText.Text = "—";
            TemplatePreviewCanvas.Children.Clear();
            return;
        }

        TemplateNameText.Text = active.Name;
        TemplateLabelSizeText.Text = $"{active.LabelWidthMm:0.#} × {active.LabelHeightMm:0.#} мм";
        TemplateDmSizeText.Text = $"{active.DataMatrixWidthMm:0.#} × {active.DataMatrixHeightMm:0.#} мм";
        TemplatePositionText.Text = $"{active.DataMatrixXmm:0.#} / {active.DataMatrixYmm:0.#} мм";
        TemplateBlocksText.Text = active.TextBlockCount.ToString();
        DrawActiveTemplatePreview(active);
    }

    private Border BuildTemplateRow(TemplateViewItem template)
    {
        var border = new Border
        {
            Style = (Style)FindResource("DataPill"),
            Margin = new Thickness(0, 0, 0, 12),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = template.IsActive ? "Активный шаблон" : "Сделать активным шаблоном",
            BorderBrush = template.IsActive
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrushSoft"),
            Background = template.IsActive
                ? (Brush)new BrushConverter().ConvertFrom("#12243B")!
                : (Brush)FindResource("PanelAltBrush")
        };
        border.MouseLeftButtonUp += (_, _) => TemplateSelected?.Invoke(this, template.Name);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = template.IsActive ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Stroke = template.IsActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrushSoft"),
            StrokeThickness = 2
        };
        grid.Children.Add(dot);

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = template.Name,
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм · DM {template.DataMatrixWidthMm:0.#} мм",
            Style = (Style)FindResource("MutedText")
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var preview = new Border
        {
            Width = 86,
            Height = 56,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)new BrushConverter().ConvertFrom("#E8EEF5")!,
            Child = BuildMiniMatrix(30, new Thickness(8, 0, 0, 0))
        };
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);

        border.Child = grid;
        return border;
    }

    private void DrawActiveTemplatePreview(TemplateViewItem template)
    {
        TemplatePreviewCanvas.Children.Clear();
        const double maxWidth = 284;
        const double maxHeight = 194;
        var scale = Math.Min(maxWidth / template.LabelWidthMm, maxHeight / template.LabelHeightMm);
        if (double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        var labelWidth = template.LabelWidthMm * scale;
        var labelHeight = template.LabelHeightMm * scale;
        var offsetX = (maxWidth - labelWidth) / 2;
        var offsetY = (maxHeight - labelHeight) / 2;

        var label = new Rectangle
        {
            Width = labelWidth,
            Height = labelHeight,
            RadiusX = 8,
            RadiusY = 8,
            Fill = Brushes.White,
            Stroke = (Brush)new BrushConverter().ConvertFrom("#D6DEE8")!,
            StrokeThickness = 1
        };
        Canvas.SetLeft(label, offsetX);
        Canvas.SetTop(label, offsetY);
        TemplatePreviewCanvas.Children.Add(label);

        var dmSize = Math.Min(template.DataMatrixWidthMm * scale, template.DataMatrixHeightMm * scale);
        var dm = BuildMatrixCanvas(dmSize);
        Canvas.SetLeft(dm, offsetX + template.DataMatrixXmm * scale);
        Canvas.SetTop(dm, offsetY + template.DataMatrixYmm * scale);
        TemplatePreviewCanvas.Children.Add(dm);

        var name = new TextBlock
        {
            Text = "GTIN 0460",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#344054")!,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Width = Math.Max(80, labelWidth - template.DataMatrixXmm * scale - dmSize - 8),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(name, Math.Min(offsetX + template.LabelWidthMm * scale - name.Width - 6, offsetX + template.DataMatrixXmm * scale + dmSize + 8));
        Canvas.SetTop(name, offsetY + Math.Max(4, template.DataMatrixYmm * scale));
        TemplatePreviewCanvas.Children.Add(name);

        var serial = new TextBlock
        {
            Text = "SN 5H0QHE",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#344054")!,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Width = name.Width,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(serial, Canvas.GetLeft(name));
        Canvas.SetTop(serial, Canvas.GetTop(name) + 20);
        TemplatePreviewCanvas.Children.Add(serial);

        var date = new TextBlock
        {
            Text = "2026-05-17 13:07",
            Foreground = (Brush)new BrushConverter().ConvertFrom("#344054")!,
            FontSize = 8,
            Width = Math.Max(120, labelWidth - 12),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(date, offsetX + 8);
        Canvas.SetTop(date, offsetY + labelHeight - 20);
        TemplatePreviewCanvas.Children.Add(date);
    }

    private static FrameworkElement BuildMiniMatrix(double size, Thickness margin)
    {
        var canvas = BuildMatrixCanvas(size);
        canvas.HorizontalAlignment = HorizontalAlignment.Left;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        canvas.Margin = margin;
        return canvas;
    }

    private static Canvas BuildMatrixCanvas(double size)
    {
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            Background = Brushes.White
        };

        var dark = (Brush)new BrushConverter().ConvertFrom("#101820")!;
        const int cells = 18;
        var cell = size / cells;
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var finder = x == 0 || y == 0 || (x == cells - 1 && y % 2 == 0) || (y == cells - 1 && x % 2 == 0);
                var data = ((x * 7 + y * 11 + x * y) % 5) < 2;
                if (!finder && !data)
                    continue;

                var rect = new Rectangle
                {
                    Width = Math.Ceiling(cell),
                    Height = Math.Ceiling(cell),
                    Fill = dark
                };
                Canvas.SetLeft(rect, x * cell);
                Canvas.SetTop(rect, y * cell);
                canvas.Children.Add(rect);
            }
        }

        return canvas;
    }

    private void OnManageTemplatesProxyClick(object sender, RoutedEventArgs e) =>
        ManageTemplatesRequested?.Invoke(sender, e);
}
