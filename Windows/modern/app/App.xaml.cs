using System;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace EasyEject.Modern {
    public partial class App : Application {
        MainWindow window;
        AppInstance instance;
        public App() { InitializeComponent(); }
        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            instance = AppInstance.FindOrRegisterForKey("EasyEject.Modern.InterfacePreview");
            if (!instance.IsCurrent) {
                await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Exit(); return;
            }
            instance.Activated += delegate { if (window != null) window.Reveal(); };
            window = new MainWindow();
            window.Closed += delegate { instance.UnregisterKey(); };
            window.Activate();
        }
    }
}
