using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DubliMark.Desktop;

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

    public static bool GetIsDragArea(DependencyObject obj) =>
        (bool)obj.GetValue(IsDragAreaProperty);

    public static void SetIsDragArea(DependencyObject obj, bool value) =>
        obj.SetValue(IsDragAreaProperty, value);

    public static DialogWindowCommand GetWindowCommand(DependencyObject obj) =>
        (DialogWindowCommand)obj.GetValue(WindowCommandProperty);

    public static void SetWindowCommand(DependencyObject obj, DialogWindowCommand value) =>
        obj.SetValue(WindowCommandProperty, value);

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
