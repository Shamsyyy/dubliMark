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

public sealed record TemplateTextBlockEdit(
    string Text,
    double Xmm,
    double Ymm,
    double FontSizePt,
    bool Bold = false,
    TextBlockLayout Layout = TextBlockLayout.Horizontal,
    TextFlowDirection Flow = TextFlowDirection.Right,
    bool Enabled = true,
    int RowIndex = -1,
    LabelFontId FontId = LabelFontId.ArialIndustrial);

public sealed record TemplateTextEdit(IReadOnlyList<TemplateTextBlockEdit> Blocks);

public partial class TemplatesView : UserControl
{
    private const double TemplatesListMaxHeight = 320;

    private bool _isSyncing;
    private TextBox? _focusedTextBox;
    private readonly List<TextBlockEditorRow> _textBlockRows = new();

    private static readonly (string Label, string InsertText)[] TextPlaceholders =
    [
        ("GTIN", "GTIN {gtin}"),
        ("SN", "SN {serial}"),
        ("Дата", "{date}"),
        ("Время", "{time}"),
        ("Дата+время", "{date} {time}"),
        ("Отгрузка", "OTGR {shipment}"),
        ("Заказ", "ORD {order}"),
        ("AI91", "{ai91}"),
        ("Тип", "{codeType}"),
        ("Источник", "{source}")
    ];

    private sealed class TextBlockEditorRow
    {
        public required CheckBox EnabledCheck { get; init; }
        public required TextBox TextBox { get; init; }
        public required TextBox XBox { get; init; }
        public required TextBox YBox { get; init; }
        public required TextBox SizeBox { get; init; }
        public required Button LayoutButton { get; init; }
        public required Button FlowButton { get; init; }
        public TextBlockLayout Layout { get; set; } = TextBlockLayout.Horizontal;
        public TextFlowDirection Flow { get; set; } = TextFlowDirection.Right;
        public bool Enabled { get; set; } = true;
        public LabelFontId FontId { get; set; } = LabelFontId.ArialIndustrial;
    }

    public event RoutedEventHandler? ManageTemplatesRequested;
    public event EventHandler<string>? TemplateSelected;
    public event EventHandler? CreateTemplateRequested;
    public event EventHandler? CopyTemplateRequested;
    public event EventHandler? DeleteTemplateRequested;
    public event EventHandler<(double W, double H)>? DmPresetRequested;
    public event EventHandler<TemplateLayoutEdit>? ApplyLayoutRequested;
    public event EventHandler<TemplateLayoutEdit>? LayoutEditedRequested;
    public event EventHandler<TemplateLayoutEdit>? LayoutCommittedRequested;
    public event EventHandler<TemplateTextEdit>? TextBlocksEditedRequested;
    public event EventHandler<TemplateTextEdit>? TextBlocksCommittedRequested;
    public event EventHandler? PrintPreviewRequested;

    public TemplatesView()
    {
        _isSyncing = true;
        InitializeComponent();
        LayoutCanvas.TextBlocksEdited += OnCanvasTextBlocksEdited;
        LayoutCanvas.TextBlocksCommitted += OnCanvasTextBlocksCommitted;
        LayoutCanvas.LayoutEdited += OnCanvasLayoutEdited;
        LayoutCanvas.LayoutCommitted += OnCanvasLayoutCommitted;
        LabelWidthSlider.ValueChanged += OnSliderValueChanged;
        LabelHeightSlider.ValueChanged += OnSliderValueChanged;
        DmWidthSlider.ValueChanged += OnSliderValueChanged;
        DmHeightSlider.ValueChanged += OnSliderValueChanged;
        DmXSlider.ValueChanged += OnSliderValueChanged;
        DmYSlider.ValueChanged += OnSliderValueChanged;
        DmXBox.LostFocus += (_, _) => OnLayoutFieldsCommitted();
        DmYBox.LostFocus += (_, _) => OnLayoutFieldsCommitted();
        TemplatesListPanel.SizeChanged += (_, _) => UpdateTemplatesListScroll();
        PopulateLabelFontCombo();
        _isSyncing = false;
        RenderPlaceholderButtons();
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

            SetDimensionFields(
                state.LabelWidthMm,
                state.LabelHeightMm,
                state.DataMatrixWidthMm,
                state.DataMatrixHeightMm,
                state.DataMatrixXmm,
                state.DataMatrixYmm);

            RenderTextBlockEditors(state.TextBlocks);
            RenderPlaceholderButtons();
            SyncLabelFontCombo(state.TextBlocks);

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

        UpdateTemplatesListScroll();
    }

    public TemplateLayoutEdit GetLayoutEdit() => new(
        ParseSize(LabelWidthBox.Text, LabelWidthSlider.Value),
        ParseSize(LabelHeightBox.Text, LabelHeightSlider.Value),
        ParseSize(DmWidthBox.Text, DmWidthSlider.Value),
        ParseSize(DmHeightBox.Text, DmHeightSlider.Value),
        ParsePos(DmXBox.Text, DmXSlider.Value),
        ParsePos(DmYBox.Text, DmYSlider.Value));

    public TemplateTextEdit GetTextBlocksEdit() => new(
        _textBlockRows.Select((row, index) => new TemplateTextBlockEdit(
            row.TextBox.Text,
            ParsePos(row.XBox.Text, 0),
            ParsePos(row.YBox.Text, 0),
            ParseSize(row.SizeBox.Text, 4),
            false,
            row.Layout,
            row.Flow,
            row.EnabledCheck.IsChecked == true,
            index,
            row.FontId)).ToList());

    private void PopulateLabelFontCombo()
    {
        LabelFontCombo.ItemsSource = LabelFontRegistry.All;
        LabelFontCombo.DisplayMemberPath = nameof(LabelFontRegistry.FontOption.DisplayName);
        if (LabelFontCombo.SelectedItem == null && LabelFontRegistry.All.Count > 0)
            LabelFontCombo.SelectedItem = LabelFontRegistry.All[0];
    }

    private void SyncLabelFontCombo(IReadOnlyList<TemplateTextBlockViewItem> blocks)
    {
        var fontId = blocks.FirstOrDefault()?.FontId ?? LabelFontId.ArialIndustrial;
        _isSyncing = true;
        try
        {
            LabelFontCombo.SelectedItem = LabelFontRegistry.Resolve(fontId);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private LabelFontId GetSelectedLabelFont()
    {
        if (LabelFontCombo.SelectedItem is LabelFontRegistry.FontOption option)
            return option.Id;
        return LabelFontId.ArialIndustrial;
    }

    private void OnLabelFontSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing)
            return;

        if (LabelFontCombo.SelectedItem is not LabelFontRegistry.FontOption option)
            return;

        foreach (var row in _textBlockRows)
            row.FontId = option.Id;

        ReloadLayoutCanvasEnabledOnly();
        SyncCanvasFontCombo(option.Id);
        TextBlocksEditedRequested?.Invoke(this, GetTextBlocksEdit());
    }

    private void SyncCanvasFontCombo(LabelFontId fontId)
    {
        LayoutCanvas.SetFontSelection(fontId);
    }

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
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var (layout, flow) = TextBlockStyleHelper.GetStyle(block.Layout, block.Flow, block.Orientation);

            var enabledCheck = new CheckBox
            {
                IsChecked = block.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Показывать эту строку на этикетке"
            };
            Grid.SetColumn(enabledCheck, 0);
            row.Children.Add(enabledCheck);

            var textBorder = CreateEditorBox(block.Text, out var textBox);
            Grid.SetColumn(textBorder, 2);
            row.Children.Add(textBorder);

            var xBorder = CreateEditorBox(Format(block.Xmm), out var xBox);
            Grid.SetColumn(xBorder, 4);
            row.Children.Add(xBorder);

            var yBorder = CreateEditorBox(Format(block.Ymm), out var yBox);
            Grid.SetColumn(yBorder, 6);
            row.Children.Add(yBorder);

            var sizeBorder = CreateEditorBox(Format(block.FontSizePt), out var sizeBox);
            Grid.SetColumn(sizeBorder, 8);
            row.Children.Add(sizeBorder);

            var layoutButton = new Button
            {
                Content = LayoutLabel(layout),
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Ориентация блока"
            };
            Grid.SetColumn(layoutButton, 10);
            row.Children.Add(layoutButton);

            var flowButton = new Button
            {
                Content = FlowLabel(flow),
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Направление текста: → ← ↓ ↑"
            };
            Grid.SetColumn(flowButton, 12);
            row.Children.Add(flowButton);

            var deleteButton = new Button
            {
                Content = "×",
                Style = (Style)FindResource("DangerButton"),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Удалить строку"
            };
            Grid.SetColumn(deleteButton, 14);
            row.Children.Add(deleteButton);

            TextBlocksEditorPanel.Children.Add(new TextBlock
            {
                Text = $"Строка {i + 1}: ✓ · текст · X · Y · pt · блок · текст · ✕",
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 4)
            });
            TextBlocksEditorPanel.Children.Add(row);

            var rowRef = new TextBlockEditorRow
            {
                EnabledCheck = enabledCheck,
                TextBox = textBox,
                XBox = xBox,
                YBox = yBox,
                SizeBox = sizeBox,
                LayoutButton = layoutButton,
                FlowButton = flowButton,
                Layout = layout,
                Flow = flow,
                Enabled = block.Enabled
            };
            rowRef.FontId = block.FontId;
            enabledCheck.Checked += (_, _) => OnRowEnabledChanged(rowRef);
            enabledCheck.Unchecked += (_, _) => OnRowEnabledChanged(rowRef);
            textBox.TextChanged += (_, _) => OnRowFieldsEdited();
            textBox.LostFocus += (_, _) => OnRowFieldsCommitted();
            xBox.TextChanged += (_, _) => OnRowFieldsEdited();
            xBox.LostFocus += (_, _) => OnRowFieldsCommitted();
            yBox.TextChanged += (_, _) => OnRowFieldsEdited();
            yBox.LostFocus += (_, _) => OnRowFieldsCommitted();
            sizeBox.TextChanged += (_, _) => OnRowFieldsEdited();
            sizeBox.LostFocus += (_, _) => OnRowFieldsCommitted();
            layoutButton.Click += (_, _) =>
            {
                rowRef.Layout = TextBlockStyleHelper.ToggleLayout(rowRef.Layout, rowRef.Flow);
                layoutButton.Content = LayoutLabel(rowRef.Layout);
                OnRowFieldsEdited();
            };
            flowButton.Click += (_, _) =>
            {
                rowRef.Flow = (TextFlowDirection)(((int)rowRef.Flow + 1) % 4);
                flowButton.Content = FlowLabel(rowRef.Flow);
                OnRowFieldsEdited();
            };
            deleteButton.Click += (_, _) => OnDeleteTextRow(rowRef);
            _textBlockRows.Add(rowRef);
        }
    }

    private void UpdateTemplatesListScroll()
    {
        if (TemplatesListPanel.Children.Count == 0)
        {
            TemplatesListScroll.ClearValue(FrameworkElement.MaxHeightProperty);
            TemplatesListScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }

        var width = TemplatesListScroll.ActualWidth;
        if (width <= 0 || double.IsNaN(width))
            width = ActualWidth > 0 ? ActualWidth : 320;

        TemplatesListPanel.Measure(new Size(width, double.PositiveInfinity));
        var contentHeight = TemplatesListPanel.DesiredSize.Height;
        if (contentHeight <= 0 || double.IsNaN(contentHeight))
            contentHeight = TemplatesListPanel.ActualHeight;

        if (contentHeight <= 0 || double.IsNaN(contentHeight))
            return;

        if (contentHeight > TemplatesListMaxHeight)
        {
            TemplatesListScroll.MaxHeight = TemplatesListMaxHeight;
            TemplatesListScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        else
        {
            TemplatesListScroll.ClearValue(FrameworkElement.MaxHeightProperty);
            TemplatesListScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private void OnDeleteTextRow(TextBlockEditorRow row)
    {
        if (_isSyncing)
            return;

        var index = _textBlockRows.IndexOf(row);
        if (index < 0)
            return;

        var blocks = CollectRowViews();
        blocks.RemoveAt(index);
        RenderTextBlockEditors(blocks);
        ReloadLayoutCanvasEnabledOnly();
        TextBlocksCommittedRequested?.Invoke(this, GetTextBlocksEdit());
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
        var textBox = inner;
        textBox.GotFocus += (_, _) => _focusedTextBox = textBox;
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

        if (ReferenceEquals(sender, DmXSlider) || ReferenceEquals(sender, DmYSlider))
            OnLayoutFieldsEdited();
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

        if (ReferenceEquals(box, DmXBox) || ReferenceEquals(box, DmYBox))
            OnLayoutFieldsEdited();
    }

    private void OnCanvasLayoutEdited(object? sender, TemplateLayoutEdit layout)
    {
        if (_isSyncing)
            return;

        SyncDmFieldsFromLayout(layout);
        LayoutEditedRequested?.Invoke(this, GetLayoutEdit());
    }

    private void OnCanvasLayoutCommitted(object? sender, TemplateLayoutEdit layout)
    {
        if (_isSyncing)
            return;

        SyncDmFieldsFromLayout(layout);
        LayoutCommittedRequested?.Invoke(this, GetLayoutEdit());
    }

    private void SyncDmFieldsFromLayout(TemplateLayoutEdit layout)
    {
        _isSyncing = true;
        try
        {
            DmXSlider.Value = ClampPos(layout.DataMatrixXmm);
            DmYSlider.Value = ClampPos(layout.DataMatrixYmm);
            DmXBox.Text = Format(DmXSlider.Value);
            DmYBox.Text = Format(DmYSlider.Value);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnLayoutFieldsEdited()
    {
        if (_isSyncing)
            return;

        ReloadLayoutCanvasEnabledOnly();
        LayoutEditedRequested?.Invoke(this, GetLayoutEdit());
    }

    private void OnLayoutFieldsCommitted()
    {
        if (_isSyncing)
            return;

        LayoutCommittedRequested?.Invoke(this, GetLayoutEdit());
    }

    private void OnRowFieldsEdited()
    {
        if (_isSyncing)
            return;

        ReloadLayoutCanvasEnabledOnly();
        TextBlocksEditedRequested?.Invoke(this, GetTextBlocksEdit());
    }

    private void OnRowFieldsCommitted()
    {
        if (_isSyncing)
            return;

        TextBlocksCommittedRequested?.Invoke(this, GetTextBlocksEdit());
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

        _isSyncing = true;
        try
        {
            SyncEditorRowsFromCanvas(edit);
            if (_textBlockRows.Count > 0)
                LabelFontCombo.SelectedItem = LabelFontRegistry.Resolve(_textBlockRows[0].FontId);
        }
        finally
        {
            _isSyncing = false;
        }

        TextBlocksEditedRequested?.Invoke(this, GetTextBlocksEdit());
    }

    private void OnCanvasTextBlocksCommitted(object? sender, TemplateTextEdit edit)
    {
        if (_isSyncing)
            return;

        _isSyncing = true;
        try
        {
            SyncEditorRowsFromCanvas(edit);
        }
        finally
        {
            _isSyncing = false;
        }

        TextBlocksCommittedRequested?.Invoke(this, GetTextBlocksEdit());
    }

    private void SyncEditorRowsFromCanvas(TemplateTextEdit edit)
    {
        foreach (var block in edit.Blocks)
        {
            var rowIndex = block.RowIndex;
            if (rowIndex < 0 || rowIndex >= _textBlockRows.Count)
                continue;

            var row = _textBlockRows[rowIndex];
            row.XBox.Text = Format(block.Xmm);
            row.YBox.Text = Format(block.Ymm);
            row.SizeBox.Text = Format(block.FontSizePt);
            row.Layout = block.Layout;
            row.Flow = block.Flow;
            row.LayoutButton.Content = LayoutLabel(block.Layout);
            row.FlowButton.Content = FlowLabel(block.Flow);
            row.FontId = block.FontId;
        }
    }

    private static string LayoutLabel(TextBlockLayout layout) =>
        layout == TextBlockLayout.Vertical ? "В" : "Г";

    private static string FlowLabel(TextFlowDirection flow) => flow switch
    {
        TextFlowDirection.Left => "←",
        TextFlowDirection.Up => "↑",
        TextFlowDirection.Down => "↓",
        _ => "→"
    };

    private void OnAddTextBlockClick(object sender, RoutedEventArgs e)
    {
        var blocks = CollectRowViews();
        blocks.Add(new TemplateTextBlockViewItem(
            "Текст ",
            0,
            0,
            4,
            PreviewText: "Текст ",
            Enabled: true,
            FontId: GetSelectedLabelFont()));
        RenderTextBlockEditors(blocks);
        ReloadLayoutCanvasEnabledOnly();
        OnRowFieldsEdited();
        if (_textBlockRows.Count > 0)
            _textBlockRows[^1].TextBox.Focus();
    }

    private void RenderPlaceholderButtons()
    {
        PlaceholderButtonsPanel.Children.Clear();
        foreach (var (label, insertText) in TextPlaceholders)
        {
            var button = new Button
            {
                Content = label,
                Tag = insertText,
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = $"Вставить: {insertText}"
            };
            button.Click += OnPlaceholderClick;
            PlaceholderButtonsPanel.Children.Add(button);
        }
    }

    private void OnPlaceholderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string insertText })
            return;

        InsertIntoFocusedTextBox(insertText);
    }

    private void InsertIntoFocusedTextBox(string insertText)
    {
        var target = _focusedTextBox ?? _textBlockRows.LastOrDefault()?.TextBox;
        if (target == null)
            return;

        var caret = target.SelectionStart;
        var text = target.Text;
        if (caret < 0 || caret > text.Length)
            caret = text.Length;

        target.Text = text.Insert(caret, insertText);
        target.SelectionStart = caret + insertText.Length;
        target.Focus();
        OnRowFieldsEdited();
    }

    private void OnRowEnabledChanged(TextBlockEditorRow row)
    {
        if (_isSyncing)
            return;

        row.Enabled = row.EnabledCheck.IsChecked == true;
        ReloadLayoutCanvasEnabledOnly();
        TextBlocksEditedRequested?.Invoke(this, GetTextBlocksEdit());
    }

    private void ReloadLayoutCanvasEnabledOnly()
    {
        LayoutCanvas.LoadLayout(
            ParseSize(LabelWidthBox.Text, LabelWidthSlider.Value),
            ParseSize(LabelHeightBox.Text, LabelHeightSlider.Value),
            ParseSize(DmWidthBox.Text, DmWidthSlider.Value),
            ParseSize(DmHeightBox.Text, DmHeightSlider.Value),
            ParsePos(DmXBox.Text, DmXSlider.Value),
            ParsePos(DmYBox.Text, DmYSlider.Value),
            CollectRowViews());
    }

    private List<TemplateTextBlockViewItem> CollectRowViews()
    {
        if (_textBlockRows.Count == 0)
            return new List<TemplateTextBlockViewItem>();

        return _textBlockRows.Select(row => new TemplateTextBlockViewItem(
            row.TextBox.Text,
            ParsePos(row.XBox.Text, 0),
            ParsePos(row.YBox.Text, 0),
            ParseSize(row.SizeBox.Text, 4),
            false,
            PreviewText: row.TextBox.Text,
            row.Layout,
            row.Flow,
            null,
            row.EnabledCheck.IsChecked == true,
            row.FontId)).ToList();
    }

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
