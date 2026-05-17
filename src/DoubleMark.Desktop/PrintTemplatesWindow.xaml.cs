using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop;

public partial class PrintTemplatesWindow : Window
{
    private readonly List<PrintTemplate> _templates;
    private readonly string? _initialActiveTemplateName;

    public IReadOnlyList<PrintTemplate> Templates => _templates;
    public string? SelectedTemplateName { get; private set; }

    public PrintTemplatesWindow(IReadOnlyList<PrintTemplate> templates, string? activeTemplateName = null)
    {
        _initialActiveTemplateName = activeTemplateName;
        _templates = templates.Count > 0
            ? templates.ToList()
            : PrintTemplateService.CreateDefaultTemplates();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshList();
        var activeIndex = _templates.FindIndex(t =>
            string.Equals(t.Name, _initialActiveTemplateName, StringComparison.OrdinalIgnoreCase));
        if (activeIndex >= 0)
            TemplateList.SelectedIndex = activeIndex;
        else if (_templates.Count > 0)
            TemplateList.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        TemplateList.ItemsSource = null;
        TemplateList.ItemsSource = _templates.Select(t => t.Name).ToList();
    }

    private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedIndex < 0 || TemplateList.SelectedIndex >= _templates.Count)
            return;

        LoadTemplate(_templates[TemplateList.SelectedIndex]);
        SelectedTemplateName = _templates[TemplateList.SelectedIndex].Name;
    }

    private void LoadTemplate(PrintTemplate template)
    {
        NameText.Text = template.Name;
        LabelWidthText.Text = F(template.LabelWidthMm);
        LabelHeightText.Text = F(template.LabelHeightMm);
        DmWidthText.Text = F(template.DataMatrixWidthMm);
        DmHeightText.Text = F(template.DataMatrixHeightMm);
        DmXText.Text = F(template.DataMatrixXmm);
        DmYText.Text = F(template.DataMatrixYmm);
        MarginText.Text = F(template.MarginMm);
        CopiesText.Text = template.DefaultCopies.ToString(CultureInfo.InvariantCulture);
        RotationCombo.SelectedIndex = template.RotationDegrees switch
        {
            90 => 1,
            180 => 2,
            270 => 3,
            _ => 0
        };
        TextBlocksText.Text = string.Join(Environment.NewLine, template.TextBlocks.Select(t =>
            $"{t.Text}|{F(t.Xmm)}|{F(t.Ymm)}|{F(t.FontSizePt)}|{t.Bold}"));
        DrawPreview(template);
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        _templates.Add(PrintTemplateService.CreateDefaultTemplates()[0] with { Name = "Новый шаблон" });
        RefreshList();
        TemplateList.SelectedIndex = _templates.Count - 1;
        SelectedTemplateName = _templates[^1].Name;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedIndex < 0)
            return;

        var copy = BuildTemplateFromFields() with { Name = NameText.Text + " копия" };
        _templates.Add(copy);
        RefreshList();
        TemplateList.SelectedIndex = _templates.Count - 1;
        SelectedTemplateName = copy.Name;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedIndex < 0 || _templates.Count <= 1)
            return;

        _templates.RemoveAt(TemplateList.SelectedIndex);
        RefreshList();
        TemplateList.SelectedIndex = 0;
        SelectedTemplateName = _templates[0].Name;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedIndex < 0)
            return;

        var template = BuildTemplateFromFields();
        if (!PrintTemplateService.IsUsable(template))
        {
            StatusText.Text = "Шаблон некорректен.";
            return;
        }

        _templates[TemplateList.SelectedIndex] = template;
        RefreshList();
        TemplateList.SelectedItem = template.Name;
        SelectedTemplateName = template.Name;
        DrawPreview(template);
        StatusText.Text = "Шаблон обновлен.";
    }

    private PrintTemplate BuildTemplateFromFields()
    {
        var rotation = RotationCombo.SelectedItem is ComboBoxItem item
            ? int.Parse(item.Content.ToString() ?? "0", CultureInfo.InvariantCulture)
            : 0;

        return new PrintTemplate
        {
            Name = string.IsNullOrWhiteSpace(NameText.Text) ? "Шаблон" : NameText.Text.Trim(),
            LabelWidthMm = D(LabelWidthText.Text, 30),
            LabelHeightMm = D(LabelHeightText.Text, 20),
            DataMatrixWidthMm = D(DmWidthText.Text, 14),
            DataMatrixHeightMm = D(DmHeightText.Text, 14),
            DataMatrixXmm = D(DmXText.Text, 2),
            DataMatrixYmm = D(DmYText.Text, 3),
            MarginMm = D(MarginText.Text, 1),
            RotationDegrees = rotation,
            DefaultCopies = Math.Max(1, I(CopiesText.Text, 1)),
            TextBlocks = ParseTextBlocks()
        };
    }

    private List<PrintTextBlock> ParseTextBlocks()
    {
        var result = new List<PrintTextBlock>();
        foreach (var line in TextBlocksText.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 5)
                continue;

            result.Add(new PrintTextBlock
            {
                Text = parts[0],
                Xmm = D(parts[1], 0),
                Ymm = D(parts[2], 0),
                FontSizePt = D(parts[3], 6),
                Bold = bool.TryParse(parts[4], out var bold) && bold
            });
        }

        return result;
    }

    private void DrawPreview(PrintTemplate template)
    {
        PreviewCanvas.Children.Clear();
        var scale = Math.Min(PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth / template.LabelWidthMm : 10,
            PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight / template.LabelHeightMm : 10);
        if (double.IsInfinity(scale) || scale <= 0)
            scale = 8;

        PreviewCanvas.Width = template.LabelWidthMm * scale;
        PreviewCanvas.Height = template.LabelHeightMm * scale;

        var dm = new Rectangle
        {
            Width = template.DataMatrixWidthMm * scale,
            Height = template.DataMatrixHeightMm * scale,
            Stroke = Brushes.Black,
            Fill = Brushes.LightGray
        };
        Canvas.SetLeft(dm, template.DataMatrixXmm * scale);
        Canvas.SetTop(dm, template.DataMatrixYmm * scale);
        PreviewCanvas.Children.Add(dm);

        foreach (var block in template.TextBlocks)
        {
            var text = new TextBlock
            {
                Text = block.Text,
                Foreground = Brushes.Black,
                FontSize = Math.Max(6, block.FontSizePt * 1.3),
                FontWeight = block.Bold ? FontWeights.Bold : FontWeights.Normal
            };
            Canvas.SetLeft(text, block.Xmm * scale);
            Canvas.SetTop(text, block.Ymm * scale);
            PreviewCanvas.Children.Add(text);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedIndex >= 0)
        {
            _templates[TemplateList.SelectedIndex] = BuildTemplateFromFields();
            SelectedTemplateName = _templates[TemplateList.SelectedIndex].Name;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static double D(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    private static int I(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
