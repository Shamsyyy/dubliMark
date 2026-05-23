using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DoubleMark.Core.Print;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Views;

public partial class HistoryView : UserControl
{
    private const int MaxRowsRendered = 100;
    private const double PreviewCollapsed = 52;
    private const double PreviewExpanded = 168;
    private static readonly TimeSpan PreviewAnimDuration = TimeSpan.FromMilliseconds(240);
    private static readonly Color AccentGlowColor = Color.FromRgb(0x2F, 0x80, 0xED);

    private readonly List<ScanHistoryItem> _items = new();
    private readonly Dictionary<string, ImageSource> _previewCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image> _previewImages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private bool _updating;
    private bool _isSignedIn = true;
    private PrintTemplateService? _templateService;
    private int _previewGeneration;

    public event EventHandler<ScanHistoryItem>? CopyRequested;
    public event EventHandler<ScanHistoryItem>? ReprintRequested;
    public event EventHandler<ScanHistoryItem>? DeleteRequested;
    public event EventHandler? ClearHistoryRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler? BrowseFolderRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler<IReadOnlyList<ScanHistoryItem>>? ExportSelectedRequested;

    public bool CloudStorageEnabled => CloudStorageCheck.IsChecked == true;
    public bool LocalStorageEnabled => LocalStorageCheck.IsChecked == true;
    public HistoryViewMode SelectedViewMode =>
        ViewCloudRadio.IsChecked == true ? HistoryViewMode.Cloud : HistoryViewMode.Local;

    public HistoryView()
    {
        InitializeComponent();
        HistoryStatusFilter.ItemsSource = new[] { "Все статусы", "Успешно", "Предупреждение", "Ошибка" };
        HistoryStatusFilter.SelectedIndex = 0;
        HistorySearchBox.Tag = "Поиск по GTIN, serial, источнику...";
    }

    public void ConfigurePreview(PrintTemplateService templateService) =>
        _templateService = templateService;

    public void ApplySettings(
        bool cloudEnabled,
        bool localEnabled,
        HistoryViewMode viewMode,
        string browseDirectory)
    {
        _updating = true;
        try
        {
            CloudStorageCheck.IsChecked = cloudEnabled;
            LocalStorageCheck.IsChecked = localEnabled;
            ViewCloudRadio.IsChecked = viewMode == HistoryViewMode.Cloud;
            ViewLocalRadio.IsChecked = viewMode == HistoryViewMode.Local;
            LocalFolderText.Text = "Папка: " + browseDirectory;
        }
        finally
        {
            _updating = false;
        }
    }

    public void SetUsage(string usageText, int count, int limit, bool showLimitWarning = true)
    {
        HistoryUsageText.Text = usageText;
        HistoryLimitWarningText.Visibility = showLimitWarning && count >= limit
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showLimitWarning && count >= limit)
        {
            HistoryLimitWarningText.Text =
                "Лимит облачной истории заполнен. При новых сканах самые старые записи будут удаляться автоматически.";
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
        var generation = ++_previewGeneration;
        HistoryRowsPanel.Children.Clear();
        _previewImages.Clear();

        var query = HistorySearchBox.Text?.Trim() ?? "";
        var status = HistoryStatusFilter.SelectedItem as string ?? "Все статусы";
        var filtered = _items
            .Where(item => MatchesQuery(item, query) && MatchesStatus(item, status))
            .Take(MaxRowsRendered)
            .ToList();

        HistoryEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count == 0)
        {
            if (SelectedViewMode == HistoryViewMode.Cloud && !_isSignedIn)
                HistoryEmptyText.Text = "Войдите в аккаунт для облачной истории или переключитесь на локальный режим.";
            else if (SelectedViewMode == HistoryViewMode.Cloud && !CloudStorageEnabled)
                HistoryEmptyText.Text = "Облачное хранение отключено в настройках ниже.";
            else if (SelectedViewMode == HistoryViewMode.Local && !LocalStorageEnabled)
                HistoryEmptyText.Text = "Локальное хранение отключено. Включите «Локально на ПК» или выберите папку.";
            else
                HistoryEmptyText.Text = "История пока пустая. Отсканируйте код ЧЗ или выберите папку с export JSON.";
        }

        foreach (var item in filtered)
            HistoryRowsPanel.Children.Add(BuildRow(item));

        UpdateSelectionUi();
        _ = LoadPreviewsAsync(filtered, generation);
    }

    private async Task LoadPreviewsAsync(IReadOnlyList<ScanHistoryItem> items, int generation)
    {
        if (_templateService == null)
            return;

        foreach (var item in items)
        {
            if (generation != _previewGeneration)
                return;

            if (item.StatusKind == UiStatusKind.Error)
                continue;

            var key = PreviewKey(item);
            if (_previewCache.TryGetValue(key, out var cached))
            {
                ApplyPreview(key, cached, generation);
                continue;
            }

            var preview = await Task.Run(() => ScanHistoryPreviewService.TryCreatePreview(item, _templateService));
            if (generation != _previewGeneration || preview == null)
                continue;

            _previewCache[key] = preview;
            ApplyPreview(key, preview, generation);
        }
    }

    private void ApplyPreview(string key, ImageSource preview, int generation)
    {
        if (generation != _previewGeneration)
            return;

        if (_previewImages.TryGetValue(key, out var image))
            image.Source = preview;
    }

    private static string PreviewKey(ScanHistoryItem item) =>
        ScanHistoryItemBuilder.DedupeKey(item);

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
        var key = PreviewKey(item);
        var isExpanded = _expandedKeys.Contains(key);
        var isSelected = _selectedKeys.Contains(key);
        var previewSize = isExpanded ? PreviewExpanded : PreviewCollapsed;
        var imageSize = previewSize - 8;

        var grid = new Grid { MinHeight = isExpanded ? PreviewExpanded + 8 : 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = previewSize });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420), MinWidth = 380 });

        AddCell(grid, ScanHistoryFormats.FormatTimestamp(item.Timestamp), 0);
        AddCell(grid, item.Source, 1);
        AddCell(grid, $"{item.Gtin}\n{item.Serial}", 2);

        var previewImage = new Image
        {
            Width = imageSize,
            Height = imageSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        if (_previewCache.TryGetValue(key, out var cachedPreview))
            previewImage.Source = cachedPreview;
        _previewImages[key] = previewImage;

        var previewHost = new Border
        {
            Width = previewSize,
            Height = previewSize,
            Background = (Brush)FindResource("SoftPanelBrush"),
            BorderBrush = isExpanded
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(isExpanded ? 1.5 : 1),
            CornerRadius = new CornerRadius(isExpanded ? 10 : 6),
            Padding = new Thickness(4),
            Child = previewImage,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = key,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        if (isExpanded)
        {
            previewHost.Effect = CreateGlowEffect(0.5);
            Panel.SetZIndex(previewHost, 20);
        }

        previewHost.MouseLeftButtonDown += (_, e) =>
        {
            if (previewImage.Source == null)
                return;

            TogglePreviewExpand(key, previewHost, previewImage, grid);
            e.Handled = true;
        };

        Grid.SetColumn(previewHost, 3);
        grid.Children.Add(previewHost);

        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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
        Grid.SetColumn(details, 4);
        grid.Children.Add(details);

        var rowShell = new Border
        {
            Style = (Style)FindResource("SoftSurface"),
            Padding = new Thickness(12, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 8),
            ClipToBounds = false,
            Tag = key,
            Child = grid
        };

        var selectButton = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = isSelected ? "✓ Выбрано" : "Выделить",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Tag = item
        };
        selectButton.Click += (_, _) => ToggleSelection(key, rowShell, selectButton);

        var actions = new WrapPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal
        };
        actions.Children.Add(selectButton);
        actions.Children.Add(MakeActionButton("Копировать", item, (_, _) => CopyRequested?.Invoke(this, item)));
        actions.Children.Add(MakeActionButton("Печать", item, (_, _) => ReprintRequested?.Invoke(this, item)));
        actions.Children.Add(MakeActionButton("Удалить", item, (_, _) => DeleteRequested?.Invoke(this, item)));
        Grid.SetColumn(actions, 5);
        grid.Children.Add(actions);

        ApplyRowSelectionVisual(rowShell, selectButton, isSelected);
        return rowShell;
    }

    private void TogglePreviewExpand(string key, Border previewHost, Image previewImage, Grid rowGrid)
    {
        var expanding = !_expandedKeys.Contains(key);
        if (expanding)
            _expandedKeys.Add(key);
        else
            _expandedKeys.Remove(key);

        var targetHost = expanding ? PreviewExpanded : PreviewCollapsed;
        var targetImage = targetHost - 8;

        AnimateSize(previewHost, targetHost);
        AnimateSize(previewImage, targetImage);
        AnimateMinHeight(rowGrid, expanding ? PreviewExpanded + 8 : 52);

        if (expanding)
        {
            previewHost.BorderBrush = (Brush)FindResource("AccentBrush");
            previewHost.BorderThickness = new Thickness(1.5);
            previewHost.CornerRadius = new CornerRadius(10);
            previewHost.Effect = CreateGlowEffect(0.55);
            Panel.SetZIndex(previewHost, 20);
        }
        else
        {
            previewHost.BorderBrush = (Brush)FindResource("BorderBrushSoft");
            previewHost.BorderThickness = new Thickness(1);
            previewHost.CornerRadius = new CornerRadius(6);
            previewHost.Effect = null;
            Panel.SetZIndex(previewHost, 0);
        }
    }

    private void ToggleSelection(string key, Border rowShell, Button selectButton)
    {
        if (_selectedKeys.Contains(key))
        {
            _selectedKeys.Remove(key);
            ApplyRowSelectionVisual(rowShell, selectButton, selected: false);
        }
        else
        {
            _selectedKeys.Add(key);
            ApplyRowSelectionVisual(rowShell, selectButton, selected: true);
        }

        UpdateSelectionUi();
    }

    private void ApplyRowSelectionVisual(Border rowShell, Button selectButton, bool selected)
    {
        if (selected)
        {
            rowShell.BorderBrush = (Brush)FindResource("AccentBrush");
            rowShell.BorderThickness = new Thickness(1.5);
            rowShell.Background = (Brush)FindResource("AccentSoftBrush");
            rowShell.Effect = CreateGlowEffect(0.38);
            selectButton.Content = "✓ Выбрано";
            selectButton.BorderBrush = (Brush)FindResource("AccentBrush");
        }
        else
        {
            rowShell.ClearValue(Border.BorderBrushProperty);
            rowShell.ClearValue(Border.BorderThicknessProperty);
            rowShell.ClearValue(Border.BackgroundProperty);
            rowShell.Effect = null;
            selectButton.Content = "Выделить";
            selectButton.ClearValue(Button.BorderBrushProperty);
        }
    }

    private void UpdateSelectionUi()
    {
        var count = _selectedKeys.Count;
        ExportSelectedButton.IsEnabled = count > 0;
        SelectionHintText.Text = count == 0
            ? "Выделите нужные ЧЗ кнопкой «Выделить», затем сохраните в отдельную папку."
            : $"Выделено: {count}. Нажмите «Сохранить выделенные в папку…» для экспорта.";
    }

    private static DropShadowEffect CreateGlowEffect(double opacity) =>
        new()
        {
            Color = AccentGlowColor,
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = opacity
        };

    private static void AnimateSize(FrameworkElement element, double target)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = PreviewAnimDuration,
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };

        element.BeginAnimation(FrameworkElement.WidthProperty, animation);
        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    private static void AnimateMinHeight(FrameworkElement element, double target)
    {
        element.BeginAnimation(
            FrameworkElement.MinHeightProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                To = target,
                Duration = PreviewAnimDuration,
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
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

    private void OnExportSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selectedKeys.Count == 0)
            return;

        var selected = _items
            .Where(item => _selectedKeys.Contains(PreviewKey(item)))
            .ToList();
        if (selected.Count > 0)
            ExportSelectedRequested?.Invoke(this, selected);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            RefreshRows();
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e) =>
        ClearHistoryRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsControlChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e) =>
        BrowseFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnReloadClick(object sender, RoutedEventArgs e) =>
        ReloadRequested?.Invoke(this, EventArgs.Empty);
}
