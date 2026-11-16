using System;
using System.Windows;
using Microsoft.Practices.ServiceLocation;
using Samba.Infrastructure.Messaging;
using Samba.Presentation.Common.ErrorReport;
using Samba.Presentation.Services;
using Samba.Services.Common;

namespace Samba.Presentation
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
#if (DEBUG)
            RunInDebugMode();
#else
            RunInReleaseMode();
#endif
            ServiceLocator.Current.GetInstance<IApplicationState>().NotifyEvent(RuleEventNames.ApplicationStarted, new { Arguments = string.Join(" ", e.Args) });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            MessagingClient.Stop();
            ServiceLocator.Current.GetInstance<ITriggerService>().CloseTriggers();
        }

        private static void RunInDebugMode()
        {
            var bootstrapper = new Bootstrapper();
            bootstrapper.Run();
        }

        private static void RunInReleaseMode()
        {
            AppDomain.CurrentDomain.UnhandledException += AppDomainUnhandledException;
            try
            {
                System.IO.File.AppendAllText("STARTUP_TRACE.txt", "1. RunInReleaseMode başladı\n");
                var bootstrapper = new Bootstrapper();
                System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2. Bootstrapper oluşturuldu, Run çağrılıyor\n");
                bootstrapper.Run();
                System.IO.File.AppendAllText("STARTUP_TRACE.txt", "3. Bootstrapper.Run tamamlandı\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("STARTUP_TRACE.txt", "HATA: " + ex.ToString() + "\n");
                HandleException(ex);
            }
        }

        private static void AppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleException(e.ExceptionObject as Exception);
        }

        private static void HandleException(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CRASH_DUMP_LOG.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine("ZAMAN: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine("HATA: " + ex.Message);
                sb.AppendLine("TUR: " + ex.GetType().FullName);
                sb.AppendLine("STACK TRACE:\n" + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("INNER HATA: " + ex.InnerException.Message);
                    sb.AppendLine("INNER STACK TRACE:\n" + ex.InnerException.StackTrace);
                }
                sb.AppendLine("==================================================\n");
                System.IO.File.AppendAllText(logFile, sb.ToString());
            }
            catch { }

            ExceptionReporter.Show(ex);
            Environment.Exit(1);
        }
    }
}
