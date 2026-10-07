using System;

namespace EasyEject {
    internal enum ImportDefaultChoice { VideosOnly, IncludePreviews, CleanLayout, AutomaticImport, AskToEject }

    // A UI draft owns no device authority. Confirmation binds the exact draft/revision;
    // neither a delayed dialog nor a changed media scope may enable removal on a new draft.
    internal sealed class ImportDefaultsEdit {
        internal sealed class Review {
            internal readonly ImportDefaultsEdit Owner;
            internal readonly long Revision;
            internal readonly bool Recovery, VideosOnly;
            internal Review(ImportDefaultsEdit owner, long revision, bool recovery, bool videosOnly) {
                Owner = owner; Revision = revision; Recovery = recovery; VideosOnly = videosOnly;
            }
        }
        readonly object gate = new object();
        readonly Action<ImportDefaults> persist;
        readonly ImportDefaults draft;
        long revision; bool closed, saving;
        internal ImportDefaultsEdit(ImportDefaults initial, Action<ImportDefaults> persist) {
            if (initial == null || persist == null) throw new ArgumentNullException();
            this.persist = persist;
            var options = initial.Snapshot();
            draft = new ImportDefaults { VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews,
                CleanLayout = options.CleanLayout, AutomaticImport = initial.AutomaticEnabled, AskToEject = options.AskToEject,
                DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash };
        }
        internal bool Closed { get { lock (gate) return closed; } }
        internal bool Saving { get { lock (gate) return saving; } }
        ImportDefaults Copy() { return new ImportDefaults { VideosOnly = draft.VideosOnly, IncludePreviews = draft.IncludePreviews,
            CleanLayout = draft.CleanLayout, AutomaticImport = draft.AutomaticImport, AskToEject = draft.AskToEject,
            DeleteOriginals = draft.DeleteOriginals, RecoverTrash = draft.RecoverTrash }; }
        internal ImportDefaults Snapshot() { lock (gate) return Copy(); }
        void Editable() { if (closed || saving) throw new InvalidOperationException("These defaults are closed or being saved. Open a fresh review."); }
        internal void Set(ImportDefaultChoice choice, bool value) {
            lock (gate) {
                Editable();
                switch (choice) {
                    case ImportDefaultChoice.VideosOnly: draft.SetVideosOnly(value); break;
                    case ImportDefaultChoice.IncludePreviews: draft.IncludePreviews = value; break;
                    case ImportDefaultChoice.CleanLayout: draft.CleanLayout = value; break;
                    case ImportDefaultChoice.AutomaticImport: draft.AutomaticImport = value; break;
                    case ImportDefaultChoice.AskToEject: draft.AskToEject = value; break;
                    default: throw new ArgumentOutOfRangeException("choice");
                }
                revision++;
            }
        }
        internal void DisableRemoval(bool recovery) {
            lock (gate) { Editable(); if (recovery) draft.RecoverTrash = false; else draft.DeleteOriginals = false; revision++; }
        }
        internal Review BeginRemovalReview(bool recovery) {
            lock (gate) { Editable(); return new Review(this, revision, recovery, draft.Snapshot().VideosOnly); }
        }
        internal bool AcceptRemoval(Review review, bool confirmed) {
            lock (gate) {
                if (review == null || review.Owner != this || review.Revision != revision || closed || saving) return false;
                revision++; // Cancel also consumes the reviewed choice; no later callback may replay it.
                if (!confirmed) return false;
                if (review.Recovery) draft.RecoverTrash = true; else draft.DeleteOriginals = true;
                return true;
            }
        }
        internal bool Cancel() { lock (gate) { if (saving) return false; closed = true; revision++; return true; } }
        internal void Save() {
            ImportDefaults frozen;
            lock (gate) { Editable(); frozen = Copy(); saving = true; revision++; }
            bool success = false;
            try { persist(frozen); success = true; }
            finally { lock (gate) { saving = false; if (success) closed = true; } }
        }
    }
}
