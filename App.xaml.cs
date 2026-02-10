using System;
using System.Windows;

namespace WindowsLiveCaptionsReader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Global exception handler
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                MessageBox.Show($"Fatal Error: {ex.ExceptionObject}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show($"UI Error: {ex.Exception.Message}\n\nStack:\n{ex.Exception.StackTrace}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
