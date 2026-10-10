using System;
using System.IO;

namespace EasyEject {
    internal enum ProfileReviewKind { RemoveOriginals, RecoverTrash, MacOwner, Forget }
    // A configuration draft is not permission to copy, export or remove a device.
    internal sealed class ImportProfileEdit {
        internal sealed class Review {
            internal readonly ImportProfileEdit Owner; internal readonly long Revision;
            internal readonly ProfileReviewKind Kind; internal readonly string MacOwner;
            internal readonly bool VideosOnly;
            internal Review(ImportProfileEdit owner, long revision, ProfileReviewKind kind, string macOwner) {
                Owner = owner; Revision = revision; Kind = kind; MacOwner = macOwner; VideosOnly = owner.Snapshot().Resolve(owner.defaults).VideosOnly;
            }
        }
        readonly object gate = new object(); ImportDefaults defaults; readonly ImportProfile draft;
        readonly Action<ImportProfile, bool> persist; readonly Action forget; readonly string sourceRoot;
        ImportDefaultsEdit choices; long revision; bool closed, saving, destinationChosen;
        internal ImportProfileEdit(ImportProfile initial, ImportDefaults defaults, string sourceRoot,
            Action<ImportProfile, bool> persist, Action forget) {
            if (initial == null || defaults == null || persist == null) throw new ArgumentNullException();
            draft = initial.Copy(); QuickShareProfile.Validate(draft);
            var options = defaults.Snapshot(); this.defaults = new ImportDefaults { VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews,
                CleanLayout = options.CleanLayout, AutomaticImport = defaults.AutomaticEnabled, AskToEject = options.AskToEject,
                DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash };
            this.sourceRoot = sourceRoot == null ? null : SafePaths.Normalize(sourceRoot); this.persist = persist; this.forget = forget; ResetChoices();
        }
        internal bool Closed { get { lock (gate) return closed; } }
        internal bool Saving { get { lock (gate) return saving; } }
        internal bool Connected { get { return sourceRoot != null; } }
        internal bool CanForget { get { return forget != null; } }
        void Editable() { if (closed || saving) throw new InvalidOperationException("This profile draft is closed or being saved. Review it again."); }
        void ResetChoices() {
            var options = draft.Resolve(defaults);
            if (choices != null) choices.Cancel();
            choices = new ImportDefaultsEdit(new ImportDefaults { VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews,
                CleanLayout = options.CleanLayout, AskToEject = options.AskToEject, AutomaticImport = draft.AutomaticEnabled(defaults),
                DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash }, value => {});
        }
        ImportProfile Copy() {
            var value = draft.Copy();
            if (!draft.FollowsDefaults) {
                var options = choices.Snapshot(); value.VideosOnly = options.VideosOnly; value.IncludePreviews = options.IncludePreviews;
                value.CleanLayout = options.CleanLayout; value.AutomaticImport = options.AutomaticEnabled; value.AskToEject = options.AskToEject;
                value.DeleteOriginals = options.DeleteOriginals; value.RecoverTrash = options.RecoverTrash;
            }
            return value;
        }
        internal ImportProfile Snapshot() { lock (gate) return Copy(); }
        internal MediaOptions Options { get { lock (gate) return Copy().Resolve(defaults); } }
        internal bool Automatic { get { lock (gate) return Copy().AutomaticEnabled(defaults); } }
        internal string SourceDisplay { get { lock (gate) return sourceRoot == null ? String.IsNullOrEmpty(draft.RelativeSource) ? "Whole device" : draft.RelativeSource : Path.Combine(sourceRoot, draft.RelativeSource ?? ""); } }
        internal void SetName(string name) { lock (gate) { Editable(); draft.Name = name; revision++; } }
        internal void SetFollowing(bool following) {
            lock (gate) { Editable(); var saved = Copy(); draft.VideosOnly = saved.VideosOnly; draft.IncludePreviews = saved.IncludePreviews;
                draft.CleanLayout = saved.CleanLayout; draft.AutomaticImport = saved.AutomaticImport; draft.AskToEject = saved.AskToEject;
                draft.DeleteOriginals = saved.DeleteOriginals; draft.RecoverTrash = saved.RecoverTrash; draft.SetCustom(!following, defaults); revision++; ResetChoices(); }
        }
        internal void UpdateDefaults(ImportDefaults value) {
            lock (gate) {
                Editable(); if (value == null) throw new ArgumentNullException("value"); var options = value.Snapshot();
                defaults = new ImportDefaults { VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout,
                    AskToEject = options.AskToEject, AutomaticImport = value.AutomaticEnabled, DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash };
                revision++; if (draft.FollowsDefaults) ResetChoices();
            }
        }
        internal void SetChoice(ImportDefaultChoice choice, bool value) { lock (gate) { Editable(); if (draft.FollowsDefaults) throw new SafetyException("Choose custom settings to edit this device's import choices."); choices.Set(choice, value); revision++; } }
        internal void DisableRemoval(bool recovery) { lock (gate) { Editable(); if (draft.FollowsDefaults) throw new SafetyException("Edit shared defaults to change this choice."); choices.DisableRemoval(recovery); revision++; } }
        internal void SetDestination(string path) { lock (gate) { Editable(); draft.Destination = SafePaths.Normalize(path); draft.SavedDestination = null; destinationChosen = true; revision++; } }
        internal void SetSource(string path) {
            lock (gate) { Editable(); if (!Connected) throw new SafetyException("Connect the original device to change its media folder.");
                path = SafePaths.Normalize(path); if (!SafePaths.Within(path, sourceRoot)) throw new SafetyException("Choose a media folder within the selected device.");
                draft.RelativeSource = path.TrimEnd('\\').Equals(sourceRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : SafePaths.Relative(path, sourceRoot); revision++; }
        }
        internal void SetSharing(bool enabled, bool uhd, bool lut) { SetSharing(enabled, uhd, lut, QuickShareProfile.Camera(Snapshot())); }
        internal void SetSharing(bool enabled, bool uhd, bool lut, QuickShareCamera camera) { lock (gate) { Editable(); if (!Enum.IsDefined(typeof(QuickShareCamera), camera)) throw new SafetyException("Choose a supported Quick Share camera.");
            draft.SharingPreset = enabled ? uhd ? QuickShareProfile.Uhd : QuickShareProfile.Hd : null;
            draft.SharingCamera = QuickShareProfile.CameraValue(camera); draft.SharingColor = QuickShareProfile.ColorValue(camera, lut); revision++; } }
        internal void ClearMacOwner() { lock (gate) { Editable(); draft.MacTrashUid = null; revision++; } }
        internal Review BeginReview(ProfileReviewKind kind, string ownerPath = null) {
            lock (gate) { Editable(); string owner = null;
                if ((kind == ProfileReviewKind.RemoveOriginals || kind == ProfileReviewKind.RecoverTrash) && draft.FollowsDefaults) throw new SafetyException("Edit shared defaults to change this choice.");
                if (kind == ProfileReviewKind.Forget && forget == null) throw new SafetyException("There is no saved profile to forget.");
                if (kind == ProfileReviewKind.MacOwner) {
                    if (!Connected) throw new SafetyException("Connect the original device before selecting its Mac Trash owner.");
                    string path = SafePaths.Normalize(ownerPath), parent = SafePaths.Normalize(Path.Combine(sourceRoot, ".Trashes")); owner = Path.GetFileName(path);
                    if (!String.Equals(Path.GetDirectoryName(path), parent, StringComparison.OrdinalIgnoreCase) || !DeviceTrash.ValidMacOwner(owner)) throw new SafetyException("Choose one explicit numeric owner folder directly under this device's .Trashes folder.");
                }
                return new Review(this, revision, kind, owner);
            }
        }
        bool Consume(Review review, bool confirmed) {
            if (review == null || review.Owner != this || review.Revision != revision || closed || saving) return false;
            revision++; return confirmed;
        }
        internal bool Accept(Review review, bool confirmed) {
            lock (gate) {
                if (review != null && review.Kind == ProfileReviewKind.Forget) throw new SafetyException("Use the explicit forget action.");
                if (!Consume(review, confirmed)) return false;
                if (review.Kind == ProfileReviewKind.MacOwner) draft.MacTrashUid = review.MacOwner;
                else { var choice = choices.BeginRemovalReview(review.Kind == ProfileReviewKind.RecoverTrash); choices.AcceptRemoval(choice, true); }
                return true;
            }
        }
        internal bool Cancel() { lock (gate) { if (saving) return false; closed = true; revision++; choices.Cancel(); return true; } }
        internal void Save() {
            ImportProfile frozen; bool chosen;
            lock (gate) { Editable(); frozen = Copy(); Profiles.ValidateName(frozen.Name, true); Profiles.ValidateStoredFolders(frozen); SafePaths.Normalize(frozen.Destination);
                if (!Connected && frozen.AutomaticEnabled(defaults) && frozen.SavedDestination == null && !destinationChosen) throw new SafetyException("Choose the destination again to verify its disk before enabling automatic import for this older profile.");
                chosen = destinationChosen; saving = true; revision++; }
            bool success = false; try { persist(frozen, chosen); success = true; }
            finally { lock (gate) { saving = false; if (success) { closed = true; choices.Cancel(); } } }
        }
        internal bool Forget(Review review, bool confirmed) {
            lock (gate) { if (review == null || review.Kind != ProfileReviewKind.Forget || !Consume(review, confirmed)) return false; saving = true; }
            bool success = false; try { forget(); success = true; return true; }
            finally { lock (gate) { saving = false; if (success) { closed = true; choices.Cancel(); } } }
        }
    }
}
