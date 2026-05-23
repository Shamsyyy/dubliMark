using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DoubleMark.Desktop;

public sealed class TemplatePrintPreviewWindow : Window
{
    public TemplatePrintPreviewWindow(ImageSource image, string subtitle = "")
    {
        Title = "Предпросмотр печати";
        Width = 560;
        Height = 480;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush", "#0B1220");
        Foreground = Brush("TextBrush", "#E8EEF5");
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Предпросмотр этикетки",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Brush("MutedTextBrush", "#9AA8BC"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var frame = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFrom("#D6DEE8")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        frame.Child = new Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(frame, 1);
        root.Children.Add(frame);

        var close = new Button
        {
            Content = "Закрыть",
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        };
        if (Application.Current?.TryFindResource("PrimaryButton") is Style primaryStyle)
            close.Style = primaryStyle;
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        Content = root;
    }

    public TemplatePrintPreviewWindow(byte[] pngBytes, string subtitle = "")
        : this(CreateImage(pngBytes), subtitle)
    {
    }

    private static Brush Brush(string key, string fallbackHex) =>
        Application.Current?.TryFindResource(key) as Brush
        ?? (Brush)new BrushConverter().ConvertFrom(fallbackHex)!;

    private static ImageSource CreateImage(byte[] pngBytes)
    {
        using var ms = new MemoryStream(pngBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
