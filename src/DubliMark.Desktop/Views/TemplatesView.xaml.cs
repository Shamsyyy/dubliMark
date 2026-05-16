using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DubliMark.Desktop.Views;

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
            TemplatePreviewNameText.Text = "—";
            return;
        }

        TemplateNameText.Text = active.Name;
        TemplateLabelSizeText.Text = $"{active.LabelWidthMm:0.#} × {active.LabelHeightMm:0.#} мм";
        TemplateDmSizeText.Text = $"{active.DataMatrixWidthMm:0.#} × {active.DataMatrixHeightMm:0.#} мм";
        TemplatePositionText.Text = $"{active.DataMatrixXmm:0.#} / {active.DataMatrixYmm:0.#} мм";
        TemplateBlocksText.Text = active.TextBlockCount.ToString();
        TemplatePreviewNameText.Text = active.Name;
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
            Child = new Rectangle
            {
                Width = 30,
                Height = 30,
                Fill = (Brush)new BrushConverter().ConvertFrom("#101820")!,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            }
        };
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);

        border.Child = grid;
        return border;
    }

    private void OnManageTemplatesProxyClick(object sender, RoutedEventArgs e) =>
        ManageTemplatesRequested?.Invoke(sender, e);
}
