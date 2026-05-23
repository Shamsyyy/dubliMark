using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop;

public partial class App : Application
{
    private const string AppMutexName = "DoubleMarkAppRunning";
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        _singleInstanceMutex = new Mutex(true, AppMutexName, out createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "DoubleMark уже запущен. Закройте предыдущее окно перед обновлением или повторным запуском.",
                "DoubleMark",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        LoggingService.LogStartup();
        RegisterExceptionHandlers();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); }
        catch (ApplicationException) { /* mutex may not be owned by this thread on some shutdown paths */ }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LoggingService.Error("App", "Unhandled domain exception", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LoggingService.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LoggingService.Error("App", "Dispatcher unhandled exception", e.Exception);
        ShowFriendlyError(e.Exception);
        e.Handled = true;
    }

    internal static void ShowFriendlyError(Exception ex)
    {
        try
        {
            MessageBox.Show(
                "Произошла ошибка. Подробности записаны в папку логов DoubleMark.\n\n" + ex.Message,
                "DoubleMark",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // ignored
        }
    }
}
