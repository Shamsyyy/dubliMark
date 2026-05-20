using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using DoubleMark.Desktop.Views;

namespace DoubleMark.Desktop;

public partial class MainWindow
{
    private readonly List<UiNotification> _notifications = new();
    private int _unreadNotifications;
    private bool _isSearchPopupClosing;

    private void FocusGlobalSearch()
    {
        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();
        SearchPopup.IsOpen = true;
        UpdateGlobalSearchResults();
    }

    private void OnGlobalSearchFocused(object sender, RoutedEventArgs e)
    {
        SearchPopup.IsOpen = true;
        UpdateGlobalSearchResults();
    }

    private void OnGlobalSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(GlobalSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchPopup.IsOpen = GlobalSearchBox.IsKeyboardFocused;
        UpdateGlobalSearchResults();
    }

    private void OnGlobalSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseGlobalSearchPopup();
            e.Handled = true;
        }
    }

    private void OnRootShellPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SearchPopup.IsOpen)
            return;

        if (IsWithin(GlobalSearchFrame, e.OriginalSource) ||
            (SearchPopup.Child is DependencyObject popupChild && IsWithin(popupChild, e.OriginalSource)))
            return;

        SearchPopup.IsOpen = false;
    }

    private static bool IsWithin(DependencyObject root, object? source)
    {
        if (source is not DependencyObject current)
            return false;

        while (current != null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void CloseGlobalSearchPopup()
    {
        _isSearchPopupClosing = true;
        try
        {
            SearchPopup.IsOpen = false;
            Focus();
        }
        finally
        {
            _isSearchPopupClosing = false;
        }
    }

    private void UpdateGlobalSearchResults()
    {
        if (SearchResultsPanel == null)
            return;

        SearchResultsPanel.Children.Clear();
        var query = GlobalSearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            AddSearchResult("История", "Сохранённые сканы с датой и временем", () => NavigateTo(GetHistoryView(), NavHistoryButton, "История"));
            AddSearchResult("Шаблоны", "Локальные шаблоны печати", () => NavigateTo(GetTemplatesView(), NavTemplatesButton, "Шаблоны"));
            AddSearchResult("Экспорт", _settings.EffectiveExportDirectory, () => NavigateTo(GetExportView(), NavExportButton, "Экспорт"));
            SearchEmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var item in _uiHistory.Where(h => SearchableText(h).Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
        {
            AddSearchResult(
                ScanHistoryFormats.FormatTimestamp(item.Timestamp) + " · " + item.Status,
                $"GTIN {item.Gtin} · SN {item.Serial} · GS {item.GsCount}",
                () => NavigateTo(GetHistoryView(), NavHistoryButton, "История"));
        }

        foreach (var template in _printTemplates.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(4))
        {
            AddSearchResult(
                template.Name,
                $"{template.LabelWidthMm:0.#}×{template.LabelHeightMm:0.#} мм · DM {template.DataMatrixWidthMm:0.#} мм",
                () => NavigateTo(GetTemplatesView(), NavTemplatesButton, "Шаблоны"));
        }

        foreach (var file in RecentFiles(_settings.EffectiveExportDirectory)
                     .Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .Take(4))
        {
            AddSearchResult(file, "Файл экспорта", () => NavigateTo(GetExportView(), NavExportButton, "Экспорт"));
        }

        SearchEmptyText.Visibility = SearchResultsPanel.Children.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddSearchResult(string title, string subtitle, Action action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SidebarButton"),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = (Brush)FindResource("TextBrush"),
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Style = (Style)FindResource("MutedText"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        button.Click += (_, _) =>
        {
            CloseGlobalSearchPopup();
            action();
        };
        SearchResultsPanel.Children.Add(button);
    }

    private static string SearchableText(ScanHistoryItem item) =>
        string.Join(" ", ScanHistoryFormats.FormatTimestamp(item.Timestamp), item.Status, item.Gtin, item.Serial,
            item.Ai91, item.Ai92, item.Ai93, item.GsCount, item.Source, item.CodeType, item.RawEscaped, item.RawHex,
            item.Template, item.Printer, item.SavedFolder);

    private void RecordNotification(string message, ToastKind kind)
    {
        _notifications.Insert(0, new UiNotification(DateTime.Now, message, kind));
        while (_notifications.Count > 30)
            _notifications.RemoveAt(_notifications.Count - 1);

        _unreadNotifications++;
        UpdateNotificationsUi();
    }

    private void UpdateNotificationsUi()
    {
        if (NotificationsBadge == null)
            return;

        NotificationsBadge.Visibility = _unreadNotifications > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsBadgeText.Text = Math.Min(_unreadNotifications, 99).ToString();
        NotificationsItemsPanel.Children.Clear();
        NotificationsEmptyText.Visibility = _notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in _notifications.Take(8))
        {
            var accent = ToastBrush(item.Kind);
            NotificationsItemsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("SoftSurface"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = accent,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Message,
                            Foreground = (Brush)FindResource("TextBrush"),
                            FontWeight = FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = item.Timestamp.ToString("HH:mm:ss"),
                            Style = (Style)FindResource("MutedText"),
                            Margin = new Thickness(0, 4, 0, 0)
                        }
                    }
                }
            });
        }
    }

    private Brush ToastBrush(ToastKind kind) =>
        kind switch
        {
            ToastKind.Success => BrushFromResource("SuccessBrush"),
            ToastKind.Warning => BrushFromResource("WarningBrush"),
            ToastKind.Error => BrushFromResource("DangerBrush"),
            _ => BrushFromResource("AccentBrush")
        };

    private void OnNotificationsClick(object sender, RoutedEventArgs e)
    {
        if (!_isSearchPopupClosing)
            SearchPopup.IsOpen = false;
        NotificationsPopup.IsOpen = !NotificationsPopup.IsOpen;
        _unreadNotifications = 0;
        UpdateNotificationsUi();
    }

    private void OnWorkspaceClick(object sender, RoutedEventArgs e)
    {
        SearchPopup.IsOpen = false;
        WorkspacePopup.IsOpen = !WorkspacePopup.IsOpen;
    }

    private void OnWorkspaceOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string name })
        {
            WorkspaceNameText.Text = name;
            WorkspacePopup.IsOpen = false;
            ShowToast("Рабочее пространство: " + name, ToastKind.Success);
        }
    }

    private void OnAccountMenuClick(object sender, RoutedEventArgs e)
    {
        SearchPopup.IsOpen = false;
        AccountPopup.IsOpen = !AccountPopup.IsOpen;
    }

    private void OnAccountProfileClick(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        NavigateTo(GetAccountView(), NavAccountButton, "Личный кабинет DoubleMark");
    }

    private async void OnAccountSignOutClick(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        await SignOutAndShowLogin();
    }

    private void OnDashboardScannerModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || !IsLoaded || ScannerModeCombo.SelectedIndex < 0)
            return;

        _settings.ScannerMode = ScannerModeCombo.SelectedIndex switch
        {
            1 => ScannerMode.Com,
            2 => ScannerMode.Hid,
            _ => ScannerMode.Auto
        };
        _settings.Save();
        var result = RestartScanner();
        ShowToast(result.Message, result.Success ? ToastKind.Success : ToastKind.Warning);
    }

    private sealed record UiNotification(DateTime Timestamp, string Message, ToastKind Kind);
}
