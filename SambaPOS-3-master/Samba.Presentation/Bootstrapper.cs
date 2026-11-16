using System;
using System.ComponentModel.Composition.Hosting;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Microsoft.Practices.Prism.MefExtensions;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using Samba.Infrastructure.Settings;
using Samba.Localization.Engine;
using Samba.Presentation.Common;
using Samba.Presentation.Common.ModelBase;
using Samba.Presentation.Common.Services;
using Samba.Presentation.Properties;
using Samba.Presentation.Services;
using Samba.Presentation.Services.Common;
using Samba.Presentation.Services.Common.DataGeneration;
using Samba.Services;
using Samba.Services.Common;

namespace Samba.Presentation
{
    public class Bootstrapper : MefBootstrapper
    {
        static readonly Mutex Mutex = new Mutex(true, "{aa77c8c4-b8c1-4e61-908f-48c6fb65227d}");

        protected override DependencyObject CreateShell()
        {
            return Container.GetExportedValue<Shell>();
        }

        protected override void ConfigureAggregateCatalog()
        {
            base.ConfigureAggregateCatalog();

            var path = System.IO.Path.GetDirectoryName(Application.ResourceAssembly.Location);
            if (path != null)
            {
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Persistance.dll"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Modules*"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Presentation*"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Services*"));

            }
            LocalSettings.AppPath = path;
        }

        protected override void InitializeModules()
        {
            base.InitializeModules();
            var moduleInitializationService = ServiceLocator.Current.GetInstance<IModuleInitializationService>();
            moduleInitializationService.Initialize();
        }

        protected override void InitializeShell()
        {
            try
            {
                System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.1 InitializeShell başladı\n");
            }
            catch { }
#if DEBUG
            // Bypass Singleton check
#else
            if (!Mutex.WaitOne(TimeSpan.Zero, true) && !LocalSettings.AllowMultipleClients)
            {
                try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.1.1 Mutex yakalanamadı - ikinci kopya çıkılıyor\n"); } catch { }
                NativeWin32.PostMessage((IntPtr)NativeWin32.HWND_BROADCAST, NativeWin32.WM_SHOWSAMBAPOS, IntPtr.Zero, IntPtr.Zero);
                Environment.Exit(1);
            }
#endif
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            System.Net.ServicePointManager.Expect100Continue = false;

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.2 Dil ayarı yapılıyor: " + LocalSettings.CurrentLanguage + "\n"); } catch { }
            LocalizeDictionary.ChangeLanguage(LocalSettings.CurrentLanguage);

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.3 UserIntraction alınıyor\n"); } catch { }
            InteractionService.UserIntraction = ServiceLocator.Current.GetInstance<IUserInteraction>();
            InteractionService.UserIntraction.ToggleSplashScreen();

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.4 Logger ve Messaging başlatılıyor\n"); } catch { }
            ServiceLocator.Current.GetInstance<IApplicationState>().MainDispatcher = Application.Current.Dispatcher;
            var logger = ServiceLocator.Current.GetInstance<ILogService>();

            var messagingService = ServiceLocator.Current.GetInstance<IMessagingService>();
            messagingService.RegisterMessageListener(new MessageListener());

            if (LocalSettings.StartMessagingClient)
                messagingService.StartMessagingClient();

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.5 PresentationServices.Initialize çağrılıyor\n"); } catch { }
            PresentationServices.Initialize();

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.6 base.InitializeShell çağrılıyor\n"); } catch { }
            base.InitializeShell();

            try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.7 DataCreationService başlatılıyor\n"); } catch { }
            try
            {
                var creationService = new DataCreationService();
                creationService.CreateData();
                try { System.IO.File.AppendAllText("STARTUP_TRACE.txt", "2.8 DataCreationService BAŞARIYLA BİTTİ!\n"); } catch { }
            }
            catch (Exception e)
            {
                try
                {
                    var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "STARTUP_ERROR_DUMP.txt");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine("BAŞLANGIÇ HATASI: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    sb.AppendLine("HATA: " + e.Message);
                    sb.AppendLine("KAYNAK: " + e.Source);
                    sb.AppendLine("STACK TRACE:\n" + e.StackTrace);
                    if (e.InnerException != null)
                    {
                        sb.AppendLine("İÇ HATA: " + e.InnerException.Message);
                        sb.AppendLine("İÇ STACK TRACE:\n" + e.InnerException.StackTrace);
                    }
                    sb.AppendLine("MEVCUT CONNECTION STRING: " + LocalSettings.ConnectionString);
                    sb.AppendLine("==================================================\n");
                    System.IO.File.WriteAllText(logFile, sb.ToString());
                }
                catch { }

                if (!string.IsNullOrEmpty(LocalSettings.ConnectionString))
                {
                    var connectionString =
                        InteractionService.UserIntraction.GetStringFromUser(
                        "Connection String",
                        string.Format(Resources.ConnectionStringError, e.Message),
                        LocalSettings.ConnectionString);

                    var cs = String.Join(" ", connectionString);

                    if (!string.IsNullOrEmpty(cs))
                        LocalSettings.ConnectionString = cs.Trim();

                    logger.LogError(e, Resources.RestartAppError);
                }
                else
                {
                    logger.LogError(e);
                    LocalSettings.ConnectionString = "";
                }
                LocalSettings.SaveSettings();
                Environment.Exit(1);
            }

            var rm = Container.GetExportedValue<IRegionManager>();
            rm.RegisterViewWithRegion("MessageRegion", typeof(WorkPeriodStatusView));
            rm.RegisterViewWithRegion("MessageRegion", typeof(MessageClientStatusView));

            Application.Current.MainWindow = (Shell)Shell;

            if (LocalizeDictionary.Instance.Culture.TextInfo.IsRightToLeft)
            {
                Application.Current.MainWindow.FlowDirection = FlowDirection.RightToLeft;
            }

            ServiceLocator.Current.GetInstance<ITriggerService>().UpdateCronObjects();

            InteractionService.UserIntraction.ToggleSplashScreen();
            EntityCollectionSortManager.Load(LocalSettings.DocumentPath + "\\CollectionSort.txt");
            
            if (!string.IsNullOrEmpty(LocalSettings.CallerIdDeviceName))
            {
                ServiceLocator.Current.GetInstance<IDeviceService>().InitializeDevice(LocalSettings.CallerIdDeviceName);                
            }

            Application.Current.MainWindow.Show();
            EventServiceFactory.EventService.PublishEvent(EventTopicNames.ShellInitialized);
            Mouse.UpdateCursor();
        }
    }
}
