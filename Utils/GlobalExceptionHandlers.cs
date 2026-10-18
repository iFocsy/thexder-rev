using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace Thexder.Utils
{
    internal static class GlobalExceptionHandlers
    {
        // An exception in a UI handler is logged and shown; the game keeps running if it can.
        public static void Register(Application app)
        {
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled UI exception");
            MessageBox.Show($"Thexder ran into an unexpected error:\n\n{e.Exception.Message}\n\nDetails are in the log under {AppPaths.LogDirectory}.",
                "Thexder", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
