using System;
using System.Threading;

namespace EasyEject {
    // Login changes are independent of automatic import and occur only on Save.
    internal sealed class StartupEdit {
        readonly StartupRegistration registration;
        internal readonly StartupSnapshot Snapshot;
        int phase; bool enabled, retarget;
        internal StartupEdit(StartupRegistration registration) { this.registration = registration; Snapshot = registration.Inspect(); enabled = Snapshot.Owned; }
        internal bool Enabled { get { return enabled; } }
        internal bool Available { get { return Volatile.Read(ref phase) == 0 && Snapshot.Available && !Snapshot.Conflict; } }
        internal bool CanRetarget { get { return Available && Snapshot.Owned && !Snapshot.Here; } }
        internal bool? Change { get { return retarget && enabled ? true : enabled == Snapshot.Owned ? (bool?)null : enabled; } }
        internal void Set(bool value) { if (!Available) throw new SafetyException("Login registration is unavailable or this draft closed. Open fresh Settings."); enabled = value; }
        internal void UseThisCopy() { if (!CanRetarget) throw new SafetyException("This login retarget review is unavailable. Open fresh Settings."); retarget = true; enabled = true; }
        internal StartupSnapshot Save() { return SaveReviewed(Change); }
        internal StartupSnapshot SaveReviewed(bool? reviewedChange) {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This login draft was already saved or closed. Open fresh Settings.");
            try { if (Change != reviewedChange) throw new SafetyException("The login choice changed after Save began. Open fresh Settings; registration was not changed."); return reviewedChange.HasValue ? registration.Apply(Snapshot, reviewedChange.Value) : registration.Inspect(); }
            finally { Interlocked.Exchange(ref phase, 2); }
        }
        internal void Cancel() { if (Interlocked.CompareExchange(ref phase, 2, 0) == 1) throw new InvalidOperationException("The login worker still owns this draft."); }
    }
}
