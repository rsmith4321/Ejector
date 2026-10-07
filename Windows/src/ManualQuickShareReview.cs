using System;
using System.Threading;

namespace EasyEject {
    // Each prompt owns one explicit export choice. Preparing/editing/cancelling
    // cannot run tools or grant removal authority; admission stays in the service.
    internal sealed class ManualQuickShareReview {
        readonly QuickShareOffer offer; int closed;
        internal QuickSharePreset Preset { get; private set; }
        internal QuickShareColor Color { get; private set; }
        internal CompletedBatch Batch { get { return offer.Batch; } }
        internal bool Closed { get { return Volatile.Read(ref closed) != 0; } }
        internal ManualQuickShareReview(QuickShareOffer offer) {
            if (offer == null || !QuickShareProfile.Enabled(offer.Profile)) throw new SafetyException("Quick Share is not enabled for this device.");
            this.offer = offer; Preset = QuickShareProfile.Preset(offer.Profile); Color = QuickShareProfile.Color(offer.Profile);
        }
        internal void Set(QuickSharePreset preset, QuickShareColor color) {
            if (Closed) throw new InvalidOperationException("This manual export choice is closed.");
            if (!Enum.IsDefined(typeof(QuickSharePreset), preset) || !Enum.IsDefined(typeof(QuickShareColor), color)) throw new SafetyException("Choose a supported size and color for this export.");
            Preset = preset; Color = color;
        }
        internal ManualQuickShareRequest Confirm() {
            if (Interlocked.CompareExchange(ref closed, 1, 0) != 0) throw new SafetyException("Open a fresh manual export choice before retrying.");
            return offer.Choose(Preset, Color);
        }
        internal void Cancel() { Interlocked.Exchange(ref closed, 1); }
    }
}
