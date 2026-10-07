using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    // A session/user-scoped wakeup only. No arguments, files or operation authority cross it.
    internal sealed class SessionWindowReopen : IDisposable {
        readonly EventWaitHandle signal; RegisteredWaitHandle listener; int stopped;
        static SecurityIdentifier User() { using (var identity = WindowsIdentity.GetCurrent()) return identity.User; }
        internal static string Name(string purpose) {
            if (purpose == null || purpose != "main" && !(purpose.StartsWith("fixture.", StringComparison.Ordinal) &&
                purpose.Length == 40 && IsFixtureToken(purpose.Substring(8)))) throw new ArgumentException("Invalid reopen scope.");
            return @"Local\EasyEject.Windows.Prototype.Reopen.v1." + User().Value + "." + purpose;
        }
        static bool IsFixtureToken(string token) { Guid value; return Guid.TryParseExact(token, "N", out value); }
        internal SessionWindowReopen(string purpose) {
            var security = new EventWaitHandleSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new EventWaitHandleAccessRule(User(), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            bool created; signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name(purpose), out created, security);
            if (!created) { signal.Dispose(); throw new InvalidOperationException("The reopen listener already exists. No second listener started."); }
        }
        internal void Listen(Action request) {
            if (request == null) throw new ArgumentNullException("request");
            if (Volatile.Read(ref stopped) != 0) throw new ObjectDisposedException("SessionWindowReopen");
            if (listener != null) throw new InvalidOperationException("The reopen listener is already registered.");
            listener = ThreadPool.RegisterWaitForSingleObject(signal, delegate {
                if (Volatile.Read(ref stopped) == 0) request();
            }, null, Timeout.Infinite, false);
        }
        internal static bool Request(string purpose, bool startup) {
            if (startup) return true; // A repeated login launch must not reveal the window.
            try { using (var existing = EventWaitHandle.OpenExisting(Name(purpose), EventWaitHandleRights.Modify)) return existing.Set(); }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        public void Dispose() {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            if (listener != null) listener.Unregister(null);
            signal.Dispose();
        }
    }

    // Coalesce thread-pool wakeups into one UI callback and revoke queued work on shutdown.
    internal sealed class WindowReopenRequests : IDisposable {
        readonly Func<Action, bool> dispatch; readonly Action reveal; int pending, stopped;
        internal WindowReopenRequests(Func<Action, bool> dispatch, Action reveal) {
            if (dispatch == null || reveal == null) throw new ArgumentNullException();
            this.dispatch = dispatch; this.reveal = reveal;
        }
        internal bool Request() {
            if (Volatile.Read(ref stopped) != 0) return false;
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return true;
            try {
                if (Volatile.Read(ref stopped) == 0 && dispatch(delegate {
                    try { if (Volatile.Read(ref stopped) == 0) reveal(); }
                    finally { Interlocked.Exchange(ref pending, 0); }
                })) return true;
            } catch (InvalidOperationException) { } // Includes disposed/destroyed UI handles.
            Interlocked.Exchange(ref pending, 0); return false;
        }
        public void Dispose() { Interlocked.Exchange(ref stopped, 1); }
    }

    internal static class UserWindowReveal {
        internal static Form Target(Form owner) {
            if (owner == null || owner.IsDisposed || owner.Disposing) return null;
            for (Form active = Form.ActiveForm, parent = active; parent != null; parent = parent.Owner)
                if (parent == owner && active != null && active.Visible && !active.IsDisposed) return active;
            Form target = owner;
            while (true) {
                Form next = null;
                foreach (var owned in target.OwnedForms) if (!owned.IsDisposed && !owned.Disposing && owned.Visible) next = owned;
                if (next == null) return target;
                target = next;
            }
        }
        internal static void Show(Form owner) {
            var target = Target(owner); if (target == null) return;
            if (!owner.Visible) owner.Show();
            if (target.WindowState == FormWindowState.Minimized) target.WindowState = FormWindowState.Normal;
            target.Activate();
        }
    }
}
