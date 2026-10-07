using System;
using System.Threading.Tasks;

namespace EasyEject {
    // Called on the UI owner thread. Queued ready callbacks must identify the
    // exact worker; a finished worker cannot grant another device action.
    internal sealed class ConcurrentStorageWork {
        internal readonly ConcurrentStorageState State;
        TrayOperation owner, drainedOwner; Task worker, drained;
        internal ConcurrentStorageWork(Operations operations) { State = new ConcurrentStorageState(operations); }
        internal bool Live { get { return owner != null && worker != null && !worker.IsCompleted; } }
        internal bool Available { get { try { return Live && State.HasPrimary && !State.Busy; } catch { State.ClearPrimary(); return false; } } }
        internal Task<T> Start<T>(TrayOperation token, Func<Task<T>> start, IImportGuard guard = null) {
            if (token == null || start == null || owner != null || State.Busy || drained != null && !drained.IsCompleted)
                throw new InvalidOperationException("The previous storage worker or manual review still owns its lifecycle.");
            owner = token; drainedOwner = null; drained = null; State.ClearPrimary();
            try {
                var task = start(); if (task == null) throw new InvalidOperationException("A live storage worker handle is required.");
                worker = task;
            } catch { owner = null; worker = null; State.ClearPrimary(); throw; }
            if (guard != null) Attach(token, guard); return (Task<T>)worker;
        }
        internal bool Attach(TrayOperation token, IImportGuard guard) {
            if (!Object.ReferenceEquals(token, owner) || !Live) return false;
            try { State.Attach(token, guard); return State.HasPrimary; }
            catch { State.ClearPrimary(); return false; }
        }
        internal Task Drain(TrayOperation token) {
            if (token == null) return Task.FromResult(true);
            if (Object.ReferenceEquals(token, drainedOwner)) return drained;
            if (!Object.ReferenceEquals(token, owner)) return Task.FromResult(true);
            if (worker != null && !worker.IsCompleted) throw new InvalidOperationException("Await the exact storage worker before releasing its proof or folders.");
            drainedOwner = owner; owner = null; worker = null;
            State.ClearPrimary(); drained = State.WaitForManual; return drained;
        }
    }
}
