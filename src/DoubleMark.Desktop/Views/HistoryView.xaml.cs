using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Services.Cloud;

namespace DoubleMark.Desktop.Views;

public partial class HistoryView : UserControl
{
    private const int MaxRowsRendered = 100;

    private readonly List<ScanHistoryItem> _items = new();
    private bool _updating;
    private bool _isSignedIn = true;

    public event EventHandler<ScanHistoryItem>? CopyRequested;
    public event EventHandler<ScanHistoryItem>? ReprintRequested;
    public event EventHandler<ScanHistoryItem>? DeleteRequested;
    public event EventHandler? ClearHistoryRequested;

    public HistoryView()
    {
        InitializeComponent();
        HistoryStatusFilter.ItemsSource = new[] { "Все статусы", "Успешно", "Предупреждение", "Ошибка" };
        HistoryStatusFilter.SelectedIndex = 0;
        HistorySearchBox.Tag = "Поиск по GTIN, serial, источнику...";
    }

    public void SetUsage(string usageText, int count, int limit)
    {
        HistoryUsageText.Text = usageText;
        HistoryLimitWarningText.Visibility = count >= limit ? Visibility.Visible : Visibility.Collapsed;
        if (count >= limit)
        {
            HistoryLimitWarningText.Text =
                "Лимит истории заполнен. При новых сканах самые старые записи будут удаляться автоматически.";
        }
    }

    public void SetSignedIn(bool signedIn)
    {
        _isSignedIn = signedIn;
        HistorySignedOutText.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
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
            .Take(MaxRowsRendered)
            .ToList();

        HistoryEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_isSignedIn)
            HistoryEmptyText.Text = "Войдите в аккаунт, чтобы видеть историю сканирования.";
        else if (filtered.Count == 0)
            HistoryEmptyText.Text = "История пока пустая. Отсканируйте код ЧЗ.";

        foreach (var item in filtered)
            HistoryRowsPanel.Children.Add(BuildRow(item));
    }

    private static bool MatchesStatus(ScanHistoryItem item, string status) =>
        status == "Все статусы" || item.Status.Contains(status, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesQuery(ScanHistoryItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var haystack = string.Join(" ", ScanHistoryFormats.FormatTimestamp(item.Timestamp), item.Gtin, item.Serial,
            item.Source, item.MaskedPreview, item.AiFlagsSummary);
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private Border BuildRow(ScanHistoryItem item)
    {
        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        AddCell(grid, ScanHistoryFormats.FormatTimestamp(item.Timestamp), 0);
        AddCell(grid, item.Source, 1);
        AddCell(grid, $"{item.Gtin}\n{item.Serial}", 2);
        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = item.AiFlagsSummary,
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 11,
            Opacity = 0.9
        });
        details.Children.Add(new TextBlock
        {
            Text = item.MaskedPreview,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(details, 3);
        grid.Children.Add(details);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(MakeActionButton("Копировать", item, (_, _) => CopyRequested?.Invoke(this, item)));
        actions.Children.Add(MakeActionButton("Печать", item, (_, _) => ReprintRequested?.Invoke(this, item)));
        actions.Children.Add(MakeActionButton("Удалить", item, (_, _) => DeleteRequested?.Invoke(this, item)));
        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);

        return new Border
        {
            Style = (Style)FindResource("SoftSurface"),
            Padding = new Thickness(12, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }

    private Button MakeActionButton(string text, ScanHistoryItem item, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = text,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Tag = item
        };
        button.Click += onClick;
        return button;
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
            Margin = new Thickness(0, 0, 10, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            RefreshRows();
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e) =>
        ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
}
