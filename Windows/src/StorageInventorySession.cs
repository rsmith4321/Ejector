using System;
using System.Threading.Tasks;

namespace EasyEject {
    // Owns publication of worker scans, not device operations. A late scan cannot
    // revive choices invalidated by a connection event or a closed window.
    internal sealed class StorageInventorySession : IDisposable {
        readonly object gate = new object(); readonly IDiscovery discovery;
        Inventory current; long revision, observedEpoch; bool scanning, closed, pending = true;
        string error;
        internal StorageInventorySession(IDiscovery discovery) { this.discovery = discovery; observedEpoch = discovery.Epoch; }
        internal Inventory Current { get { lock (gate) return current; } }
        internal bool Scanning { get { lock (gate) return scanning; } }
        internal bool Pending { get { lock (gate) return pending && !closed; } }
        internal bool Closed { get { lock (gate) return closed; } }
        internal string Error { get { lock (gate) return error; } }
        internal bool Observe() {
            lock (gate) {
                if (closed || observedEpoch == discovery.Epoch) return false;
                observedEpoch = discovery.Epoch; revision++; current = null; pending = true; return true;
            }
        }
        internal async Task<bool> Refresh() {
            long start;
            lock (gate) { if (closed || scanning) return false; scanning = true; pending = false; start = revision; }
            try {
                var next = await Task.Run(() => discovery.Scan()).ConfigureAwait(false);
                lock (gate) {
                    if (closed) return false;
                    if (start != revision || next.Epoch != discovery.Epoch) { current = null; pending = true; return false; }
                    current = next; observedEpoch = next.Epoch; error = null; return true;
                }
            } catch (Exception ex) {
                lock (gate) { if (!closed) { current = null; error = ex.Message; } }
                return false;
            } finally { lock (gate) scanning = false; }
        }
        public void Dispose() { lock (gate) { closed = true; current = null; pending = false; revision++; } }
    }
}
