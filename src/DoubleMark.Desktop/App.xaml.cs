using System.Windows;
using System.Windows.Threading;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LoggingService.LogStartup();
        RegisterExceptionHandlers();
        base.OnStartup(e);
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
