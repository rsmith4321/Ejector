using System;
using Microsoft.UI.Xaml;

namespace EasyEject.Modern {
    public partial class App : Application {
        MainWindow window;
        readonly TrayStartupGate lifetime; readonly bool startup; readonly Action shutdown;
        internal App(TrayStartupGate lifetime, bool startup, Action shutdown) { InitializeComponent(); this.lifetime = lifetime; this.startup = startup; this.shutdown = shutdown; }
        protected override void OnLaunched(LaunchActivatedEventArgs args) {
            lifetime.Start(startup, () => {
                window = new MainWindow(); window.Closed += delegate { shutdown(); };
            }, () => { _ = window.StartRuntime(); }, action => window.DispatcherQueue.TryEnqueue(() => action()), () => window.Reveal());
        }
    }
}
