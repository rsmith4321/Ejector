using System;

namespace EasyEject {
    public sealed class ImportWorkProgress {
        public readonly string Phase;
        public readonly double Fraction;
        public int Percent { get { return (int)Math.Floor(Fraction * 100); } }
        internal ImportWorkProgress(string phase, double fraction) { Phase = phase; Fraction = Double.IsNaN(fraction) || Double.IsInfinity(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction)); }
    }
    internal sealed class ImportWorkReporter {
        readonly Action<ImportWorkProgress> report;
        string lastPhase; int lastPercent = -1;
        internal ImportWorkReporter(Action<ImportWorkProgress> report) { this.report = report; }
        internal void Emit(string phase, double fraction) {
            if (report == null) return; var value = new ImportWorkProgress(phase, fraction);
            if (lastPhase == phase && lastPercent == value.Percent) return;
            lastPhase = phase; lastPercent = value.Percent; report(value);
        }
        // A display callback cannot turn an already published receipt into a failed import.
        internal void Complete() { try { Emit("Import complete", 1); } catch (Exception) {} }
    }
}
