using System;
using System.Threading;

namespace EasyEject {
    internal sealed class IssueReview {
        readonly AppIssue live, expected; readonly Action<AppIssue> save; readonly long revision; int phase;
        internal IssueReview(AppIssue issue, Action<AppIssue> save) { live = issue; expected = Copy(issue); revision = issue.Revision; this.save = save; }
        internal static AppIssue Copy(AppIssue issue) { return new AppIssue { Message = issue.Message, ObservedUtc = issue.ObservedUtc, Dismissed = issue.Dismissed }; }
        internal string Description { get { return expected.Message + "\n\nObserved: " + expected.ObservedUtc + "\n\n" + (expected.Dismissed ? "This issue was acknowledged. " : "") + "Dismiss acknowledges only this issue. It never retries an import, export or eject. Later failures restore attention."; } }
        internal bool CanDismiss { get { return Volatile.Read(ref phase) == 0 && expected.HasIssue && !expected.Dismissed && Current; } }
        bool Current { get { return live.Revision == revision && live.Message == expected.Message && live.ObservedUtc == expected.ObservedUtc && live.Dismissed == expected.Dismissed; } }
        internal void Dismiss() {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This issue review already closed. Open a fresh review.");
            try {
                if (!expected.HasIssue || expected.Dismissed || !Current) throw new SafetyException("The issue changed. Open a fresh review; the current issue was not acknowledged.");
                var choice = Copy(expected); choice.Dismiss(); save(choice);
                if (!Current) throw new SafetyException("Acknowledgement was saved, but a newer issue appeared. Its attention was retained; open a fresh review.");
                live.Dismiss();
            } finally { Interlocked.Exchange(ref phase, 2); }
        }
        internal void Cancel() { if (Interlocked.CompareExchange(ref phase, 2, 0) == 1) throw new InvalidOperationException("The issue save still owns this review."); }
    }

    internal sealed class DiagnosticsReview : IDisposable {
        readonly Func<DebugLogSnapshot> load; readonly Action<DebugLogSnapshot> clear;
        DebugLogSnapshot snapshot; int phase; bool attempted;
        internal DiagnosticsReview(DebugLogSnapshot initial, Func<DebugLogSnapshot> load, Action<DebugLogSnapshot> clear) {
            if (initial == null) throw new SafetyException("Debug log was not read."); snapshot = initial; this.load = load; this.clear = clear;
        }
        internal DiagnosticsReview(string root) : this(DebugLog.Load(root), () => DebugLog.Load(root), expected => DebugLog.Clear(expected, root)) { }
        internal string Text { get { return snapshot.Text; } }
        internal bool Busy { get { return Volatile.Read(ref phase) == 1; } }
        internal bool Open { get { return Volatile.Read(ref phase) == 0; } }
        internal bool CanCopy { get { return Open && snapshot.Exists; } }
        internal bool CanClear { get { return CanCopy && !attempted; } }
        void Enter() { if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This debug-log view closed or its worker is still active."); }
        internal void Refresh() {
            Enter();
            try { var value = load(); if (value == null) throw new SafetyException("Debug log could not be refreshed."); snapshot = value; attempted = false; }
            finally { Interlocked.Exchange(ref phase, 0); }
        }
        internal void Copy(Action<string> clipboard) {
            Enter(); try { if (!snapshot.Exists) throw new SafetyException("There is no displayed log to copy."); clipboard(snapshot.Text); }
            finally { Interlocked.Exchange(ref phase, 0); }
        }
        internal void Clear() {
            Enter();
            try {
                if (!snapshot.Exists || attempted) throw new SafetyException("Refresh and review the current log before clearing it.");
                attempted = true; clear(snapshot);
                try { var value = load(); if (value == null) throw new SafetyException("Debug log could not be refreshed."); snapshot = value; }
                catch (Exception ex) { throw new SafetyException("The displayed log was cleared, but refreshing its status failed. Refresh before another action. " + ex.Message); }
            } finally { Interlocked.Exchange(ref phase, 0); }
        }
        public void Dispose() { if (Interlocked.CompareExchange(ref phase, 2, 0) == 1) throw new InvalidOperationException("The debug-log worker still owns this view."); }
    }
}
