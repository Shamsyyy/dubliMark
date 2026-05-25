using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Views;

public sealed record TemplateLayoutEdit(
    double LabelWidthMm,
    double LabelHeightMm,
    double DataMatrixWidthMm,
    double DataMatrixHeightMm,
    double DataMatrixXmm,
    double DataMatrixYmm);

public sealed record LabelExtrasEdit(
    bool ShowDate,
    bool ShowShipment,
    bool ShowOrder,
    string? ShipmentNumber,
    string? OrderNumber);

public sealed record TemplateTextBlockEdit(
    string Text,
    double Xmm,
    double Ymm,
    double FontSizePt,
    bool Bold = false,
    TextBlockDirection Orientation = TextBlockDirection.LeftToRight);

public sealed record TemplateTextEdit(IReadOnlyList<TemplateTextBlockEdit> Blocks);

public partial class TemplatesView : UserControl
{
    private bool _isSyncing;
    private readonly List<TextBlockEditorRow> _textBlockRows = new();

    private sealed class TextBlockEditorRow
    {
        public required TextBox TextBox { get; init; }
        public required TextBox XBox { get; init; }
        public required TextBox YBox { get; init; }
        public required TextBox SizeBox { get; init; }
        public required Button OrientationButton { get; init; }
        public TextBlockDirection Orientation { get; set; } = TextBlockDirection.LeftToRight;
    }

    public event RoutedEventHandler? ManageTemplatesRequested;
    public event EventHandler<string>? TemplateSelected;
    public event EventHandler? CreateTemplateRequested;
    public event EventHandler? CopyTemplateRequested;
    public event EventHandler? DeleteTemplateRequested;
    public event EventHandler<(double W, double H)>? DmPresetRequested;
    public event EventHandler<TemplateLayoutEdit>? ApplyLayoutRequested;
    public event EventHandler<TemplateTextEdit>? ApplyTextBlocksRequested;
    public event EventHandler<TemplateTextEdit>? TextBlocksEditedRequested;
    public event EventHandler<TemplateTextEdit>? TextBlocksCommittedRequested;
    public event EventHandler<LabelExtrasEdit>? LabelExtrasApplyRequested;
    public event EventHandler? PrintPreviewRequested;

    public TemplatesView()
    {
        _isSyncing = true;
        InitializeComponent();
        LayoutCanvas.TextBlocksEdited += OnCanvasTextBlocksEdited;
        LayoutCanvas.TextBlocksCommitted += OnCanvasTextBlocksCommitted;
        LabelWidthSlider.ValueChanged += OnSliderValueChanged;
        LabelHeightSlider.ValueChanged += OnSliderValueChanged;
        DmWidthSlider.ValueChanged += OnSliderValueChanged;
        DmHeightSlider.ValueChanged += OnSliderValueChanged;
        DmXSlider.ValueChanged += OnSliderValueChanged;
        DmYSlider.ValueChanged += OnSliderValueChanged;
        _isSyncing = false;
    }

    public void UpdatePreviewImage(System.Windows.Media.ImageSource? image)
    {
        TemplatePreviewImage.Source = image;
        TemplatePreviewPlaceholder.Visibility = image == null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void UpdateState(TemplateViewState state)
    {
        _isSyncing = true;
        try
        {
            TemplatesSyncStatusText.Text = state.SyncStatusText;
            TemplatesListPanel.Children.Clear();

            foreach (var template in state.Templates)
                TemplatesListPanel.Children.Add(BuildTemplateRow(template));

            LabelShowDateCheck.IsChecked = state.LabelShowDate;
            LabelShowShipmentCheck.IsChecked = state.LabelShowShipment;
            LabelShowOrderCheck.IsChecked = state.LabelShowOrder;
            ShipmentNumberBox.Text = state.LabelShipmentNumber ?? "";
            OrderNumberBox.Text = state.LabelOrderNumber ?? "";

            SetDimensionFields(
                state.LabelWidthMm,
                state.LabelHeightMm,
                state.DataMatrixWidthMm,
                state.DataMatrixHeightMm,
                state.DataMatrixXmm,
                state.DataMatrixYmm);

            RenderTextBlockEditors(state.TextBlocks);

            LayoutCanvas.LoadLayout(
                state.LabelWidthMm,
                state.LabelHeightMm,
                state.DataMatrixWidthMm,
                state.DataMatrixHeightMm,
                state.DataMatrixXmm,
                state.DataMatrixYmm,
                state.TextBlocks);

            TemplatePreviewImage.Source = state.PreviewImage;
            TemplatePreviewPlaceholder.Visibility = state.PreviewImage == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public TemplateLayoutEdit GetLayoutEdit() => new(
        ParseSize(LabelWidthBox.Text, LabelWidthSlider.Value),
        ParseSize(LabelHeightBox.Text, LabelHeightSlider.Value),
        ParseSize(DmWidthBox.Text, DmWidthSlider.Value),
        ParseSize(DmHeightBox.Text, DmHeightSlider.Value),
        ParsePos(DmXBox.Text, DmXSlider.Value),
        ParsePos(DmYBox.Text, DmYSlider.Value));

    public LabelExtrasEdit GetLabelExtras() => new(
        LabelShowDateCheck.IsChecked == true,
        LabelShowShipmentCheck.IsChecked == true,
        LabelShowOrderCheck.IsChecked == true,
        ShipmentNumberBox.Text,
        OrderNumberBox.Text);

    public TemplateTextEdit GetTextBlocksEdit() => new(
        _textBlockRows.Select(row => new TemplateTextBlockEdit(
            row.TextBox.Text,
            ParsePos(row.XBox.Text, 0),
            ParsePos(row.YBox.Text, 0),
            ParseSize(row.SizeBox.Text, 4),
            false,
            row.Orientation)).ToList());

    private void RenderTextBlockEditors(IReadOnlyList<TemplateTextBlockViewItem> blocks)
    {
        TextBlocksEditorPanel.Children.Clear();
        _textBlockRows.Clear();

        if (blocks.Count == 0)
        {
            TextBlocksEditorPanel.Children.Add(new TextBlock
            {
                Text = "В шаблоне пока нет строк текста",
                Style = (Style)FindResource("MutedText")
            });
            return;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var textBorder = CreateEditorBox(block.Text, out var textBox);
            Grid.SetColumn(textBorder, 0);
            row.Children.Add(textBorder);

            var xBorder = CreateEditorBox(Format(block.Xmm), out var xBox);
            Grid.SetColumn(xBorder, 2);
            row.Children.Add(xBorder);

            var yBorder = CreateEditorBox(Format(block.Ymm), out var yBox);
            Grid.SetColumn(yBorder, 4);
            row.Children.Add(yBorder);

            var sizeBorder = CreateEditorBox(Format(block.FontSizePt), out var sizeBox);
            Grid.SetColumn(sizeBorder, 6);
            row.Children.Add(sizeBorder);

            var orientationButton = new Button
            {
                Content = DirectionLabel(block.Orientation),
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Направление: → ← ↓ ↑"
            };
            Grid.SetColumn(orientationButton, 8);
            row.Children.Add(orientationButton);

            TextBlocksEditorPanel.Children.Add(new TextBlock
            {
                Text = $"Строка {i + 1}: текст · X · Y · pt · направление",
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 4)
            });
            TextBlocksEditorPanel.Children.Add(row);

            var rowRef = new TextBlockEditorRow
            {
                TextBox = textBox,
                XBox = xBox,
                YBox = yBox,
                SizeBox = sizeBox,
                OrientationButton = orientationButton,
                Orientation = block.Orientation
            };
            orientationButton.Click += (_, _) =>
            {
                rowRef.Orientation = (TextBlockDirection)(((int)rowRef.Orientation + 1) % 4);
                orientationButton.Content = DirectionLabel(rowRef.Orientation);
            };
            _textBlockRows.Add(rowRef);
        }
    }

    private Border CreateEditorBox(string text, out TextBox inner)
    {
        inner = new TextBox
        {
            Text = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("CardBrush"),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = inner
        };
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

        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = template.IsActive ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Stroke = template.IsActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrushSoft"),
            StrokeThickness = 2
        });

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = template.Name,
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var meta = $"{template.LabelWidthMm:0.#} × {template.LabelHeightMm:0.#} мм · DM {template.DataMatrixWidthMm:0.#}×{template.DataMatrixHeightMm:0.#} мм";
        if (template.IsDefault)
            meta += " · По умолчанию";
        text.Children.Add(new TextBlock { Text = meta, Style = (Style)FindResource("MutedText") });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        border.Child = grid;
        return border;
    }

    private void SetDimensionFields(
        double labelWidth,
        double labelHeight,
        double dmWidth,
        double dmHeight,
        double dmX,
        double dmY)
    {
        LabelWidthSlider.Value = ClampSize(labelWidth);
        LabelHeightSlider.Value = ClampSize(labelHeight);
        DmWidthSlider.Value = ClampSize(dmWidth);
        DmHeightSlider.Value = ClampSize(dmHeight);
        DmXSlider.Value = ClampPos(dmX);
        DmYSlider.Value = ClampPos(dmY);

        LabelWidthBox.Text = Format(LabelWidthSlider.Value);
        LabelHeightBox.Text = Format(LabelHeightSlider.Value);
        DmWidthBox.Text = Format(DmWidthSlider.Value);
        DmHeightBox.Text = Format(DmHeightSlider.Value);
        DmXBox.Text = Format(DmXSlider.Value);
        DmYBox.Text = Format(DmYSlider.Value);
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncing)
            return;

        _isSyncing = true;
        try
        {
            if (sender == LabelWidthSlider)
                LabelWidthBox.Text = Format(e.NewValue);
            else if (sender == LabelHeightSlider)
                LabelHeightBox.Text = Format(e.NewValue);
            else if (sender == DmWidthSlider)
                DmWidthBox.Text = Format(e.NewValue);
            else if (sender == DmHeightSlider)
                DmHeightBox.Text = Format(e.NewValue);
            else if (sender == DmXSlider)
                DmXBox.Text = Format(e.NewValue);
            else if (sender == DmYSlider)
                DmYBox.Text = Format(e.NewValue);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnDimensionBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncing || sender is not TextBox box)
            return;

        var text = box.Text.Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return;

        _isSyncing = true;
        try
        {
            if (box == LabelWidthBox)
                LabelWidthSlider.Value = ClampSize(value);
            else if (box == LabelHeightBox)
                LabelHeightSlider.Value = ClampSize(value);
            else if (box == DmWidthBox)
                DmWidthSlider.Value = ClampSize(value);
            else if (box == DmHeightBox)
                DmHeightSlider.Value = ClampSize(value);
            else if (box == DmXBox)
                DmXSlider.Value = ClampPos(value);
            else if (box == DmYBox)
                DmYSlider.Value = ClampPos(value);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        var parts = tag.Split(':');
        if (parts.Length != 2)
            return;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            return;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return;

        DmPresetRequested?.Invoke(this, (w, h));
    }

    private void OnApplyLayoutClick(object sender, RoutedEventArgs e) =>
        ApplyLayoutRequested?.Invoke(this, GetLayoutEdit());

    private void OnCanvasTextBlocksEdited(object? sender, TemplateTextEdit edit)
    {
        if (_isSyncing)
            return;

        SyncEditorRowsFromCanvas(edit);
        TextBlocksEditedRequested?.Invoke(this, edit);
    }

    private void OnCanvasTextBlocksCommitted(object? sender, TemplateTextEdit edit)
    {
        if (_isSyncing)
            return;

        SyncEditorRowsFromCanvas(edit);
        TextBlocksCommittedRequested?.Invoke(this, edit);
    }

    private void SyncEditorRowsFromCanvas(TemplateTextEdit edit)
    {
        for (var i = 0; i < edit.Blocks.Count && i < _textBlockRows.Count; i++)
        {
            var block = edit.Blocks[i];
            var row = _textBlockRows[i];
            row.XBox.Text = Format(block.Xmm);
            row.YBox.Text = Format(block.Ymm);
            row.SizeBox.Text = Format(block.FontSizePt);
            row.Orientation = block.Orientation;
            row.OrientationButton.Content = DirectionLabel(block.Orientation);
        }
    }

    private static string DirectionLabel(TextBlockDirection direction) => direction switch
    {
        TextBlockDirection.RightToLeft => "←",
        TextBlockDirection.TopToBottom => "↓",
        TextBlockDirection.BottomToTop => "↑",
        _ => "→"
    };

    private void OnApplyTextBlocksClick(object sender, RoutedEventArgs e) =>
        ApplyTextBlocksRequested?.Invoke(this, GetTextBlocksEdit());

    private void OnApplyLabelExtrasClick(object sender, RoutedEventArgs e) =>
        LabelExtrasApplyRequested?.Invoke(this, GetLabelExtras());

    private void OnCreateClick(object sender, RoutedEventArgs e) => CreateTemplateRequested?.Invoke(this, EventArgs.Empty);
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyTemplateRequested?.Invoke(this, EventArgs.Empty);
    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteTemplateRequested?.Invoke(this, EventArgs.Empty);
    private void OnPrintPreviewClick(object sender, RoutedEventArgs e) => PrintPreviewRequested?.Invoke(this, EventArgs.Empty);
    private void OnManageClick(object sender, RoutedEventArgs e) => ManageTemplatesRequested?.Invoke(sender, e);

    private static double ParseSize(string text, double fallback)
    {
        var normalized = text.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return Round(ClampSize(value));
        return Round(ClampSize(fallback));
    }

    private static double ParsePos(string text, double fallback)
    {
        var normalized = text.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return Round(ClampPos(value));
        return Round(ClampPos(fallback));
    }

    private static double ClampSize(double value) => Math.Clamp(value, 0.1, 50);
    private static double ClampPos(double value) => Math.Clamp(value, 0, 50);
    private static double Round(double value) => Math.Round(value, 1);
    private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
