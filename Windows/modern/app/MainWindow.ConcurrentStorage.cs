using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        readonly ConcurrentStorageWork concurrentWork;
        readonly Forms.ToolStripMenuItem stopOtherMenu = new Forms.ToolStripMenuItem("Stop other storage action");
        ConcurrentStorageReview otherReview; ContentDialog otherDialog;
        Action refreshOtherReview; bool concurrentIssuePending;
        bool CanUseOtherStorage { get { return working && confirmation == null && otherReview == null && otherDialog == null && StorageView.Visibility == Visibility.Visible
            && notifications.Available && settings != null && concurrentWork.Available; } }
        bool CanReadStorage { get { return !UiBusy || CanUseOtherStorage; } }
        Task<T> StartStorageTask<T>(TrayOperation token, Func<Task<T>> start, IImportGuard guard = null) {
            concurrentIssuePending = false;
            var task = concurrentWork.Start(token, start, guard); UpdateActions(); return task;
        }
        void QueueStorageProof(TrayOperation token, IImportGuard guard) {
            DispatcherQueue.TryEnqueue(() => { if (!closed && concurrentWork.Attach(token, guard)) UpdateActions(); });
        }
        async Task DrainStorageTask(TrayOperation token) { var wait = concurrentWork.Drain(token); if (!closed) UpdateActions(); await wait; }
        async Task ReviewOtherStorage(bool forceClean, TrayStorageChoice choice) {
            if (!CanUseOtherStorage || inventory.Scanning || choice == null) return;
            ConcurrentStorageReview view = null; Task<ConcurrentStorageOutcome> pending = null; bool ending = false;
            try {
                view = new ConcurrentStorageReview(concurrentWork.State, inventory.Current, choice, settings, forceClean, discovery, operations,
                    validate => new WindowsEjectApi(validate), WindowsDiscovery.VolumeId);
                otherReview = view; UpdateActions(); await EnsureVisible();
                var status = Wrapped("Review every affected volume. Cancel keeps this device connected.");
                var stop = new Button { Content = "Stop this device action", IsEnabled = false };
                var stopPrimary = new Button { Content = "Stop the main transfer", IsEnabled = operationCancel != null };
                stop.Click += delegate { view.RequestStop(); status.Text = "Stopping this device action before its next request. Wait for the result before unplugging."; };
                stopPrimary.Click += delegate { RequestOperationStop(); stopPrimary.IsEnabled = false; };
                var body = new StackPanel { Spacing = 16 }; body.Children.Add(Wrapped(view.Description)); body.Children.Add(status); body.Children.Add(stop); body.Children.Add(stopPrimary);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Safe eject of another device", Content = Scrolling(body),
                    PrimaryButtonText = "Request safe removal", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                otherDialog = dialog;
                refreshOtherReview = () => { if (!ending && otherReview == view) { dialog.IsPrimaryButtonEnabled = pending == null && view.Current; stopOtherMenu.Enabled = view.Busy;
                    stopPrimary.IsEnabled = concurrentWork.Live && operationCancel != null && !operationCancel.IsCancellationRequested; } };
                dialog.PrimaryButtonClick += async (sender, args) => {
                    args.Cancel = true; if (ending || pending != null || !view.Current) return;
                    var deferral = args.GetDeferral();
                    try {
                        completions.InvalidateDisks(view.Disks);
                        pending = view.Execute(message => DispatcherQueue.TryEnqueue(() => { if (!closed && !ending && otherReview == view) status.Text = message; }));
                        dialog.IsPrimaryButtonEnabled = false; stop.IsEnabled = true; status.Text = "Requesting safe removal of this device. The main transfer continues independently."; UpdateActions();
                        var result = await pending;
                        if (!closed && !ending && otherReview == view) { status.Text = result.Message + "\n\nClose this review to return to the main transfer or its completion."; stop.IsEnabled = false; dialog.CloseButtonText = "Close"; }
                    } catch (Exception ex) {
                        view.RequestStop(); if (pending != null) await pending;
                        if (!closed && !ending && otherReview == view) { status.Text = (view.Outcome == null ? "No removal result is available. " : view.Outcome.Message + "\n") + "Review display needs attention: " + ex.Message; dialog.IsPrimaryButtonEnabled = false; dialog.CloseButtonText = "Close"; }
                    } finally { deferral.Complete(); if (!closed) UpdateActions(); }
                };
                dialog.Closing += (sender, args) => { if (view.Busy || pending != null && !pending.IsCompleted) { args.Cancel = true; view.RequestStop(); status.Text = "Stopping this device action. Waiting for its worker before closing…"; } };
                refreshOtherReview(); await dialog.ShowAsync();
            } catch (Exception ex) {
                OtherStorageStatus.Text = "Other-device review needs attention. " + ex.Message; concurrentIssuePending = true; RecordModernIssue(OtherStorageStatus.Text);
            } finally {
                ending = true; refreshOtherReview = null;
                try {
                    if (view != null) {
                        view.RequestStop(); if (pending != null) await pending;
                        try {
                            var result = view.Outcome;
                            if (result != null && !closed) {
                                OtherStorageStatus.Text = result.Message;
                                if (!result.Success && !result.Cancelled) { concurrentIssuePending = true; RecordModernIssue("Other storage action: " + result.Message); }
                                if (settings != null && settings.NotificationsEnabled) { tray.BalloonTipTitle = result.Success ? "Safe removal accepted" : result.Cancelled ? "Other-device action cancelled" : "Other device still connected"; tray.BalloonTipText = result.Message.Substring(0, Math.Min(240, result.Message.Length)); tray.ShowBalloonTip(5000); }
                            }
                        } finally { view.Dispose(); }
                    }
                } finally {
                    otherReview = null; otherDialog = null; stopOtherMenu.Enabled = false;
                    if (!closed) { UpdateActions(); await ReadInventory(); }
                }
            }
        }
    }
}
