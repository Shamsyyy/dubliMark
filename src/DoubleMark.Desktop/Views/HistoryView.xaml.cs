using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop.Views;

public partial class HistoryView : UserControl
{
    private const int MaxRowsRendered = 250;
    private const int MaxSearchMatches = 2000;

    private readonly List<ScanHistoryItem> _items = new();
    private ScanHistoryItem? _selected;
    private bool _updating;
    private int _previewGeneration;

    public PrintTemplateService? TemplateService { get; set; }

    public event EventHandler<ScanHistoryItem>? OpenFolderRequested;
    public event EventHandler<ScanHistoryItem>? CopyRequested;
    public event EventHandler<ScanHistoryItem>? ReprintRequested;

    public HistoryView()
    {
        InitializeComponent();
        HistoryStatusFilter.ItemsSource = new[] { "Все статусы", "Успешно", "Предупреждение", "Ошибка" };
        HistoryStatusFilter.SelectedIndex = 0;
        HistorySearchBox.Tag = "Поиск по GTIN, serial, GS, AI...";
    }

    public void UpdateItems(IReadOnlyList<ScanHistoryItem> items)
    {
        _updating = true;
        try
        {
            _items.Clear();
            _items.AddRange(items);
            RefreshRows();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshRows()
    {
        HistoryRowsPanel.Children.Clear();
        var query = HistorySearchBox.Text?.Trim() ?? "";
        var status = HistoryStatusFilter.SelectedItem as string ?? "Все статусы";
        var filtered = _items
            .Where(item => MatchesQuery(item, query) && MatchesStatus(item, status))
            .Take(MaxSearchMatches)
            .Take(MaxRowsRendered)
            .ToList();

        HistoryEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in filtered)
            HistoryRowsPanel.Children.Add(BuildRow(item));

        if (_selected == null || !filtered.Contains(_selected))
            SelectItem(filtered.FirstOrDefault());
    }

    private static bool MatchesStatus(ScanHistoryItem item, string status) =>
        status == "Все статусы" || item.Status.Contains(status, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesQuery(ScanHistoryItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var haystack = string.Join(" ", ScanHistoryFormats.FormatTimestamp(item.Timestamp), item.Gtin, item.Serial,
            item.Ai91, item.Ai92, item.Ai93, item.GsCount, item.Source, item.CodeType, item.RawEscaped, item.Template,
            item.Printer, item.Error);
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private Border BuildRow(ScanHistoryItem item)
    {
        var grid = new Grid { MinHeight = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });

        AddCell(grid, ScanHistoryFormats.FormatTimestamp(item.Timestamp), 0);
        AddStatusCell(grid, item, 1);
        AddCell(grid, item.Ai91, 2);
        AddCell(grid, item.Ai92 != "—" ? item.Ai92 : item.Ai93, 3);
        AddCell(grid, item.Template, 4);

        var action = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = "Открыть",
            Padding = new Thickness(10, 6, 10, 6),
            Tag = item
        };
        action.Click += (_, _) => SelectItem(item);
        Grid.SetColumn(action, 5);
        grid.Children.Add(action);

        var border = new Border
        {
            Style = (Style)FindResource("SoftSurface"),
            Padding = new Thickness(12, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = grid,
            Tag = item
        };
        border.MouseLeftButtonUp += (_, _) => SelectItem(item);
        return border;
    }

    private void AddCell(Grid grid, string text, int column)
    {
        var tb = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text) ? "—" : text,
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 12,
            Opacity = 0.88,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private void AddStatusCell(Grid grid, ScanHistoryItem item, int column)
    {
        var foreground = StatusBrush(item.StatusKind);
        var badge = new Border
        {
            Style = (Style)FindResource("PillBadge"),
            BorderBrush = foreground,
            Background = (Brush)FindResource(item.StatusKind switch
            {
                UiStatusKind.Success => "SuccessBadgeBackgroundBrush",
                UiStatusKind.Warning => "WarningBadgeBackgroundBrush",
                UiStatusKind.Error => "DangerBadgeBackgroundBrush",
                _ => "NeutralBadgeBackgroundBrush"
            }),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = item.Status,
                Foreground = foreground,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetColumn(badge, column);
        grid.Children.Add(badge);
    }

    private Brush StatusBrush(UiStatusKind kind) =>
        kind switch
        {
            UiStatusKind.Success => (Brush)FindResource("SuccessBrush"),
            UiStatusKind.Warning => (Brush)FindResource("WarningBrush"),
            UiStatusKind.Error => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("MutedTextBrush")
        };

    private void SelectItem(ScanHistoryItem? item)
    {
        _selected = item;
        var generation = ++_previewGeneration;

        if (item == null)
        {
            HistoryPreviewStatusText.Text = "—";
            HistoryPreviewTitleText.Text = "—";
            HistoryPreviewMetaText.Text = "—";
            HistoryPreviewPayloadText.Text = "—";
            HistoryPreviewImage.Source = null;
            HistoryPreviewPlaceholder.Text = "Выберите скан";
            HistoryPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        HistoryPreviewStatusText.Text = item.Status;
        HistoryPreviewStatusText.Foreground = StatusBrush(item.StatusKind);
        HistoryPreviewBadge.BorderBrush = StatusBrush(item.StatusKind);
        HistoryPreviewTitleText.Text = $"{ScanHistoryFormats.FormatTimestamp(item.Timestamp)} · {item.Source}";
        HistoryPreviewMetaText.Text = $"GTIN {item.Gtin} · SN {item.Serial} · GS {item.GsCount} · {item.CodeType}";
        HistoryPreviewPayloadText.Text = item.RawEscaped;
        HistoryPreviewImage.Source = null;
        HistoryPreviewPlaceholder.Text = "Генерация preview…";
        HistoryPreviewPlaceholder.Visibility = Visibility.Visible;

        _ = LoadPreviewAsync(item, generation);
    }

    private async Task LoadPreviewAsync(ScanHistoryItem item, int generation)
    {
        if (TemplateService == null)
        {
            ApplyPreview(item, generation, item.PreviewImage, "Нет шаблона печати");
            return;
        }

        var templates = TemplateService;
        var preview = await Task.Run(() => ScanHistoryPreviewService.TryCreatePreview(item, templates)).ConfigureAwait(true);
        ApplyPreview(item, generation, preview ?? item.PreviewImage,
            preview == null && item.PreviewImage == null ? "Preview недоступен" : "Выберите скан");
    }

    private void ApplyPreview(ScanHistoryItem item, int generation, ImageSource? preview, string emptyPlaceholder)
    {
        if (generation != _previewGeneration || !ReferenceEquals(_selected, item))
            return;

        HistoryPreviewImage.Source = preview;
        if (preview == null)
        {
            HistoryPreviewPlaceholder.Text = emptyPlaceholder;
            HistoryPreviewPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            HistoryPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            RefreshRows();
    }

    private void OnOpenSelectedFolderClick(object sender, RoutedEventArgs e)
    {
        if (_selected != null)
            OpenFolderRequested?.Invoke(this, _selected);
    }

    private void OnCopySelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selected != null)
            CopyRequested?.Invoke(this, _selected);
    }

    private void OnReprintSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selected != null)
            ReprintRequested?.Invoke(this, _selected);
    }
}
