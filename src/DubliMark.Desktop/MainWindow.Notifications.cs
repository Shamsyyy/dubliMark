using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DubliMark.Core.Export;
using DubliMark.Core.Models;
using DubliMark.Core.Print;

namespace DubliMark.Desktop;

public partial class MainWindow
{
    private readonly Queue<ToastMessage> _toastQueue = new();
    private bool _isToastVisible;

    private void ShowScanToast(ParseResult result, MarkExportResult? exportResult, PrintPipelineResult? printResult)
    {
        if (!result.IsValid)
        {
            var message = result.ErrorCode == ParseErrorCode.NoGsSeparator
                ? "Потерян GS: код не сохранен как готовый ЧЗ"
                : "Скан не прошел проверку";
            ShowToast(message, ToastKind.Warning);
            return;
        }

        ShowToast(exportResult is { Success: true } ? "Код сохранен локально" : "Код распознан", ToastKind.Success);
    }

    private void ShowToast(string message, ToastKind kind)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _toastQueue.Enqueue(new ToastMessage(message, kind));
        if (!_isToastVisible)
            ShowNextToast();
    }

    private void ShowNextToast()
    {
        if (_toastQueue.Count == 0)
        {
            _isToastVisible = false;
            return;
        }

        _isToastVisible = true;
        var toast = _toastQueue.Dequeue();
        ToastPanel.Children.Clear();
        var border = BuildToast(toast);
        border.Opacity = 0;
        border.RenderTransform = new TranslateTransform(0, 12);
        ToastPanel.Children.Add(border);

        border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)border.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) =>
            {
                ToastPanel.Children.Clear();
                ShowNextToast();
            };
            border.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    private Border BuildToast(ToastMessage toast)
    {
        var accent = toast.Kind switch
        {
            ToastKind.Success => BrushFromResource("SuccessBrush"),
            ToastKind.Warning => BrushFromResource("WarningBrush"),
            ToastKind.Error => BrushFromResource("DangerBrush"),
            _ => BrushFromResource("AccentBrush")
        };

        return new Border
        {
            Background = (Brush)FindResource("SoftPanelBrush"),
            BorderBrush = BrushFromResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("ToastShadow"),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = new GridLength(12) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new Border
                    {
                        Background = accent,
                        CornerRadius = new CornerRadius(999)
                    },
                    ToastText(toast.Message)
                }
            }
        };
    }

    private static TextBlock ToastText(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 2);
        return text;
    }

    private sealed record ToastMessage(string Message, ToastKind Kind);

    private enum ToastKind
    {
        Success,
        Warning,
        Error
    }
}
