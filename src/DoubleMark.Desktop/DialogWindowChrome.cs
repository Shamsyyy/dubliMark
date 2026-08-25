using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DoubleMark.Desktop;

public enum DialogWindowCommand
{
    None,
    Minimize,
    MaximizeRestore,
    Close
}

public static class DialogWindowChrome
{
    public static readonly DependencyProperty IsDragAreaProperty =
        DependencyProperty.RegisterAttached(
            "IsDragArea",
            typeof(bool),
            typeof(DialogWindowChrome),
            new PropertyMetadata(false, OnIsDragAreaChanged));

    public static readonly DependencyProperty WindowCommandProperty =
        DependencyProperty.RegisterAttached(
            "WindowCommand",
            typeof(DialogWindowCommand),
            typeof(DialogWindowChrome),
            new PropertyMetadata(DialogWindowCommand.None, OnWindowCommandChanged));

    public static readonly DependencyProperty AutoFitContentProperty =
        DependencyProperty.RegisterAttached(
            "AutoFitContent",
            typeof(bool),
            typeof(DialogWindowChrome),
            new PropertyMetadata(false, OnAutoFitContentChanged));

    public static bool GetIsDragArea(DependencyObject obj) =>
        (bool)obj.GetValue(IsDragAreaProperty);

    public static void SetIsDragArea(DependencyObject obj, bool value) =>
        obj.SetValue(IsDragAreaProperty, value);

    public static DialogWindowCommand GetWindowCommand(DependencyObject obj) =>
        (DialogWindowCommand)obj.GetValue(WindowCommandProperty);

    public static void SetWindowCommand(DependencyObject obj, DialogWindowCommand value) =>
        obj.SetValue(WindowCommandProperty, value);

    public static bool GetAutoFitContent(DependencyObject obj) =>
        (bool)obj.GetValue(AutoFitContentProperty);

    public static void SetAutoFitContent(DependencyObject obj, bool value) =>
        obj.SetValue(AutoFitContentProperty, value);

    private static void OnAutoFitContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.Loaded -= OnAutoFitLoaded;
        if (e.NewValue is true)
            element.Loaded += OnAutoFitLoaded;
    }

    private static void OnAutoFitLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var window = Window.GetWindow(element);
        if (window == null)
            return;

        window.Dispatcher.BeginInvoke(
            () => FitWindowToContent(window, element),
            DispatcherPriority.Loaded);
    }

    internal static void FitWindowToContent(Window window, FrameworkElement content)
    {
        if (!window.IsLoaded)
            return;

        window.UpdateLayout();
        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        if (double.IsNaN(width) || width <= 1)
            width = content.ActualWidth > 1 ? content.ActualWidth : 480;

        content.Measure(new Size(width, double.PositiveInfinity));
        var work = WindowWorkAreaHelper.GetWorkAreaDip(window);
        var maxHeight = Math.Max(160, work.Height - 48);
        var fitted = CalculateFittedHeight(window.Height, content.DesiredSize.Height, maxHeight);
        if (Math.Abs(fitted - window.Height) > 0.5)
            window.Height = fitted;

        ClampToWorkArea(window, work);
    }

    internal static double CalculateFittedHeight(
        double currentHeight,
        double contentDesiredHeight,
        double maxWorkAreaHeight,
        double extraPadding = 20)
    {
        if (double.IsNaN(currentHeight) || currentHeight < 0)
            currentHeight = 0;
        if (double.IsNaN(contentDesiredHeight) || contentDesiredHeight < 0)
            contentDesiredHeight = 0;
        if (maxWorkAreaHeight < 160)
            maxWorkAreaHeight = 160;

        var target = Math.Ceiling(contentDesiredHeight + extraPadding);
        target = Math.Max(target, currentHeight);
        return Math.Min(target, maxWorkAreaHeight);
    }

    private static void ClampToWorkArea(Window window, Rect work)
    {
        if (work.Width <= 0 || work.Height <= 0)
            return;

        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        var height = window.Height;
        if (double.IsNaN(width) || double.IsNaN(height))
            return;

        if (window.Owner != null && window.WindowStartupLocation == WindowStartupLocation.CenterOwner)
        {
            window.Left = window.Owner.Left + (window.Owner.ActualWidth - width) / 2;
            window.Top = window.Owner.Top + (window.Owner.ActualHeight - height) / 2;
        }

        if (window.Left < work.Left)
            window.Left = work.Left;
        if (window.Top < work.Top)
            window.Top = work.Top;
        if (window.Left + width > work.Right)
            window.Left = Math.Max(work.Left, work.Right - width);
        if (window.Top + height > work.Bottom)
            window.Top = Math.Max(work.Top, work.Bottom - height);
    }

    private static void OnIsDragAreaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.MouseLeftButtonDown -= OnDragAreaMouseLeftButtonDown;
        if (e.NewValue is true)
            element.MouseLeftButtonDown += OnDragAreaMouseLeftButtonDown;
    }

    private static void OnWindowCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
            return;

        button.Click -= OnCommandButtonClick;
        button.Loaded -= OnCommandButtonLoaded;
        if (e.NewValue is DialogWindowCommand.None)
            return;

        button.Click += OnCommandButtonClick;
        button.Loaded += OnCommandButtonLoaded;
    }

    private static void OnCommandButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var window = Window.GetWindow(button);
        if (window == null)
            return;

        UpdateMaximizeButton(button, window);
        window.StateChanged -= OnWindowStateChanged;
        window.StateChanged += OnWindowStateChanged;
    }

    private static void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        foreach (var button in FindVisualChildren<Button>(window))
            UpdateMaximizeButton(button, window);
    }

    private static void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var window = Window.GetWindow((DependencyObject)sender);
        if (window == null)
            return;

        if (e.ClickCount == 2 && CanMaximize(window))
        {
            ToggleWindowState(window);
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if Windows ends the mouse capture between down and drag.
            }
        }
    }

    private static void OnCommandButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var window = Window.GetWindow(button);
        if (window == null)
            return;

        switch (GetWindowCommand(button))
        {
            case DialogWindowCommand.Minimize:
                window.WindowState = WindowState.Minimized;
                break;
            case DialogWindowCommand.MaximizeRestore:
                ToggleWindowState(window);
                UpdateMaximizeButton(button, window);
                break;
            case DialogWindowCommand.Close:
                window.Close();
                break;
        }
    }

    private static bool CanMaximize(Window window) =>
        window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    private static void ToggleWindowState(Window window)
    {
        if (!CanMaximize(window))
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private static void UpdateMaximizeButton(Button button, Window window)
    {
        if (GetWindowCommand(button) != DialogWindowCommand.MaximizeRestore)
            return;

        var canMaximize = CanMaximize(window);
        button.IsEnabled = canMaximize;
        button.Content = window.WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
