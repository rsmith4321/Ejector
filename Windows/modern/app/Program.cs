using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace EasyEject.Modern {
    internal static class Program {
        [STAThread]
        static void Main(string[] args) {
            try {
                bool startup = StartupLaunch.Parse(args);
                WinRT.ComWrappersSupport.InitializeComWrappers();
                var instance = AppInstance.FindOrRegisterForKey("EasyEject.Modern.InterfacePreview");
                if (!instance.IsCurrent) {
                    // Repeated login launches are silent and never reveal a running app.
                    if (!startup) {
                        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                        Task.Run(async () => await instance.RedirectActivationToAsync(activation)).GetAwaiter().GetResult();
                    }
                    return;
                }
                var lifetime = new TrayStartupGate();
                EventHandler<AppActivationArguments> redirected = (sender, activation) => { if (activation.Kind == ExtendedActivationKind.Launch) lifetime.Request(false); };
                instance.Activated += redirected;
                Application.Start(delegate {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    new App(lifetime, startup, () => { lifetime.Dispose(); instance.Activated -= redirected; instance.UnregisterKey(); });
                });
            } catch (Exception ex) { Console.Error.WriteLine("Easy Eject could not start: " + ex.Message); Environment.ExitCode = 1; }
        }
    }
}
