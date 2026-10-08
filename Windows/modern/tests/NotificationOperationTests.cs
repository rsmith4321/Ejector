using System;
using System.IO;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        static void NotificationOperationTests() {
            Test("modern notification disabled or closed never reaches shell delivery", delegate {
                int calls = 0;
                foreach (bool closed in new[] { false, true })
                    Check(TrayNotificationDelivery.TryShow(false, closed, "Import complete", "Retained result", false,
                        (timeout, title, message, warning) => { calls++; throw new IOException("Must not deliver"); }) == null, "Disabled notification fabricated an error");
                Check(TrayNotificationDelivery.TryShow(true, true, "Import complete", "Retained result", false,
                    (timeout, title, message, warning) => calls++) == null && calls == 0, "Closed owner attempted notification");
            });
            Test("modern notification preserves result text and warning classification within shell limits", delegate {
                int calls = 0;
                string title = new string('t', 63), message = new string('m', 255);
                Check(TrayNotificationDelivery.TryShow(true, false, title, message, true, (timeout, heading, body, warning) => {
                    calls++; Check(timeout == 5000 && heading == title && body == message && warning, "Delivery changed valid result text or severity");
                }) == null && calls == 1, "Notification was skipped or retried");
                Check(TrayNotificationDelivery.TryShow(true, false, title + "extra", message + "extra", false, (timeout, heading, body, warning) => {
                    calls++; Check(heading == title && body == message && !warning, "Shell length bounds or information classification changed");
                }) == null && calls == 2, "Long result attempted multiple notifications");
            });
            Test("modern notification truncation retains complete unicode characters", delegate {
                string title = new string('t', 62) + "\ud83d\udcf7 camera", message = new string('m', 254) + "\ud83d\udcf7 result";
                Check(TrayNotificationDelivery.TryShow(true, false, title, message, false, (timeout, heading, body, warning) => {
                    Check(heading.Length == 62 && body.Length == 254 && !Char.IsSurrogate(heading[heading.Length - 1]) && !Char.IsSurrogate(body[body.Length - 1]), "Notification split a surrogate pair");
                }) == null, "Unicode result failed delivery");
            });
            Test("modern notification delivery error is returned once without a retry or thrown callback", delegate {
                int calls = 0;
                string warning = TrayNotificationDelivery.TryShow(true, false, "Import complete", "Verified copies retained", false,
                    (timeout, title, message, attention) => { calls++; throw new ObjectDisposedException("Owned fixture tray"); });
                Check(calls == 1 && warning.Contains("Owned fixture tray") && warning.Contains("Review the result in Easy Eject"), "Shell failure lost its explanation or retried");
            });
            Test("modern failed secondary notification retains accepted removal and releases exact view", delegate {
                var f = new ModernConcurrentFixture();
                var review = f.Review(); var result = review.Execute(message => {}).GetAwaiter().GetResult();
                f.Primary.SetResult(1); Task wait = f.Work.Drain(f.Token); int calls = 0;
                Check(!wait.IsCompleted && review.Outcome == result && result.Success, "Terminal view prematurely released main completion");
                string warning = TrayNotificationDelivery.TryShow(true, false, "Safe removal accepted", result.Message, false,
                    (timeout, title, message, attention) => { calls++; throw new IOException("Generated shell delivery failure"); });
                Check(warning != null && calls == 1 && review.Outcome == result && result.Success && f.Api.Requests == 1, "Optional presentation changed or reissued accepted removal");
                review.Dispose(); Check(wait.IsCompleted && f.Import.Operations.Available(new[] { 9 }), "Notification failure retained secondary lifetime or reservation"); f.Finish();
            });
            Test("modern failed secondary notification retains veto original and fresh manual retry", delegate {
                var f = new ModernConcurrentFixture(); f.Api.Success = false;
                string original = Path.Combine(f.Other, "ordinary.mp4"); File.WriteAllText(original, "retained generated original");
                using (var review = f.Review()) {
                    var result = review.Execute(message => {}).GetAwaiter().GetResult();
                    string warning = TrayNotificationDelivery.TryShow(true, false, "Other device still connected", result.Message, true,
                        (timeout, title, message, attention) => { Check(attention && message.Contains("veto"), "Veto notification changed classification/result"); throw new IOException("Generated shell failure"); });
                    Check(warning != null && !result.Success && review.Outcome == result && result.Message.Contains("veto") && f.Api.Requests == 1, "Notification failure replaced veto or retried removal");
                }
                Check(f.Work.Live && File.ReadAllText(original) == "retained generated original", "Presentation failure ended primary or changed original");
                f.Api.Success = true;
                using (var fresh = f.Review()) Check(fresh.Execute(message => {}).GetAwaiter().GetResult().Success && f.Api.Requests == 2, "Fresh manual retry was blocked after notification failure");
                f.Finish();
            });
        }
    }
}
