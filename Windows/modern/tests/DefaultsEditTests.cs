using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ImportDefaultsEdit Editor(ImportDefaults initial = null) { return new ImportDefaultsEdit(initial ?? new ImportDefaults(), value => {}); }
        static void DefaultsEditTests() {
            Test("modern draft resolves missing defaults without mutating legacy values", delegate {
                var old = new ImportDefaults { IncludePreviews = false, DeleteOriginals = true };
                var edit = Editor(old); var view = edit.Snapshot();
                Check(view.VideosOnly == true && view.IncludePreviews == false && view.CleanLayout == true && view.DeleteOriginals == true && view.RecoverTrash == false && view.AutomaticImport == false && view.AskToEject == false, "Resolved legacy choices changed");
                edit.Set(ImportDefaultChoice.VideosOnly, false);
                Check(old.VideosOnly == null && old.DeleteOriginals == true && edit.Snapshot().DeleteOriginals == false, "Draft rewrote original or broadened removal");
            });
            Test("modern editing and cancellation never invoke persistence", delegate {
                int writes = 0; var edit = new ImportDefaultsEdit(new ImportDefaults(), value => writes++);
                edit.Set(ImportDefaultChoice.AutomaticImport, true); var frozen = edit.Snapshot().Snapshot();
                edit.Set(ImportDefaultChoice.IncludePreviews, false); edit.Cancel();
                Check(writes == 0 && frozen.IncludePreviews && edit.Closed, "Draft or cancellation saved changes");
                Throws<InvalidOperationException>(() => edit.Save()); Throws<InvalidOperationException>(() => edit.Set(ImportDefaultChoice.CleanLayout, false));
            });
            Test("modern removal requires exact confirmed review and cannot replay it", delegate {
                var edit = Editor(); var review = edit.BeginRemovalReview(false);
                Check(!edit.AcceptRemoval(review, false) && edit.Snapshot().DeleteOriginals == false, "Cancel enabled removal");
                Check(!edit.AcceptRemoval(review, true), "Cancelled review replayed as confirmation");
                review = edit.BeginRemovalReview(false);
                Check(edit.AcceptRemoval(review, true) && edit.Snapshot().DeleteOriginals == true && !edit.AcceptRemoval(review, true), "Exact confirmation failed or replayed");
                edit.DisableRemoval(false); Check(edit.Snapshot().DeleteOriginals == false, "Explicit off did not disable removal");
            });
            Test("modern review cannot cross owners or survive a media scope change", delegate {
                var edit = Editor(); var foreign = Editor().BeginRemovalReview(false); var review = edit.BeginRemovalReview(false);
                Check(review.VideosOnly && !edit.AcceptRemoval(foreign, true), "Foreign review enabled removal");
                edit.Set(ImportDefaultChoice.VideosOnly, false);
                Check(!edit.AcceptRemoval(review, true) && edit.Snapshot().DeleteOriginals == false, "Old videos-only review enabled all-media removal");
            });
            Test("modern all-media transition clears both acknowledged removal choices", delegate {
                var edit = Editor(); Check(edit.AcceptRemoval(edit.BeginRemovalReview(false), true), "Removal review failed");
                Check(edit.AcceptRemoval(edit.BeginRemovalReview(true), true), "Recovery review failed");
                edit.Set(ImportDefaultChoice.VideosOnly, false); var view = edit.Snapshot();
                Check(view.DeleteOriginals == false && view.RecoverTrash == false, "All media retained destructive choices");
                edit.Set(ImportDefaultChoice.VideosOnly, true); Check(edit.Snapshot().DeleteOriginals == false && edit.Snapshot().RecoverTrash == false, "Returning to videos reenabled choices");
            });
            Test("modern changed preview selection revokes pending recovery confirmation", delegate {
                var edit = Editor(); var review = edit.BeginRemovalReview(true);
                edit.Set(ImportDefaultChoice.IncludePreviews, false);
                Check(!edit.AcceptRemoval(review, true) && edit.Snapshot().RecoverTrash == false, "Changed recovery scope accepted stale confirmation");
                Check(edit.AcceptRemoval(edit.BeginRemovalReview(true), true) && edit.Snapshot().DeleteOriginals == false, "Fresh recovery changed independent removal");
            });
            Test("modern cancelled editor rejects delayed confirmation", delegate {
                var edit = Editor(); var review = edit.BeginRemovalReview(true); Check(edit.Cancel(), "Idle Cancel refused");
                Check(!edit.AcceptRemoval(review, true) && edit.Snapshot().RecoverTrash == false, "Delayed closed dialog enabled recovery");
            });
            Test("modern save freezes choices and refuses reentrant edits or discard", delegate {
                ImportDefaultsEdit edit = null; int writes = 0; var initial = new ImportDefaults();
                edit = new ImportDefaultsEdit(initial, value => {
                    writes++; Check(edit.Saving && !edit.Cancel(), "Saving editor allowed discard");
                    Throws<InvalidOperationException>(() => edit.Set(ImportDefaultChoice.VideosOnly, false));
                    Throws<InvalidOperationException>(() => edit.Save());
                    Check(value.VideosOnly == true && value.IncludePreviews == false, "Save did not freeze exact choices");
                    value.VideosOnly = false;
                });
                edit.Set(ImportDefaultChoice.IncludePreviews, false); edit.Save();
                Check(writes == 1 && edit.Closed && !edit.Saving && edit.Snapshot().VideosOnly == true && initial.IncludePreviews == null, "Save callback mutated draft or original");
            });
            Test("modern failed save permits explicit retry but revokes old confirmations", delegate {
                int writes = 0; var edit = new ImportDefaultsEdit(new ImportDefaults(), value => { if (++writes == 1) throw new IOException("Owned save failure"); });
                var review = edit.BeginRemovalReview(false); Throws<IOException>(() => edit.Save());
                Check(!edit.Saving && !edit.Closed && !edit.AcceptRemoval(review, true), "Failed save retained busy state or old review");
                edit.Save(); Check(writes == 2 && edit.Closed, "Explicit retry did not finish");
            });
            Test("modern snapshot cannot mutate the editor or later frozen options", delegate {
                var edit = Editor(); var copy = edit.Snapshot(); var frozen = copy.Snapshot(); copy.DeleteOriginals = true;
                edit.Set(ImportDefaultChoice.VideosOnly, false);
                Check(edit.Snapshot().DeleteOriginals == false && frozen.VideosOnly && !frozen.DeleteOriginals, "Mutable snapshot changed editor or operation");
            });
            Test("modern defaults persistence preserves following and custom profile resolution", delegate {
                string root = ModernDefaultsFixture(); int writes = 0;
                var edit = new ImportDefaultsEdit(new ImportDefaults(), value => { ImportDefaults.Save(value, root); writes++; });
                edit.Set(ImportDefaultChoice.IncludePreviews, false); edit.Set(ImportDefaultChoice.AutomaticImport, true); edit.Save();
                var saved = ImportDefaults.Load(root);
                var following = new ImportProfile { UsesImportDefaults = true };
                var custom = new ImportProfile { UsesImportDefaults = false, VideosOnly = false, IncludePreviews = true, AutomaticImport = false };
                Check(writes == 1 && following.Resolve(saved).VideosOnly && !following.Resolve(saved).IncludePreviews && following.AutomaticEnabled(saved), "Following profile lost persisted choices");
                Check(!custom.Resolve(saved).VideosOnly && custom.Resolve(saved).IncludePreviews && !custom.AutomaticEnabled(saved), "Custom profile was rewritten");
                Check(!File.Exists(Path.Combine(root, "import-history.json")), "Saving defaults created import history");
            });
            Test("modern runtime copies disposable media with frozen defaults and retains originals", delegate {
                string root = ModernDefaultsFixture(), source = Path.Combine(root, "source"), destination = Path.Combine(root, "destination");
                Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
                File.WriteAllBytes(Path.Combine(source, "clip.mp4"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllBytes(Path.Combine(source, "clip.lrv"), new byte[] { 5, 6 });
                File.WriteAllBytes(Path.Combine(source, "photo.jpg"), new byte[] { 7, 8, 9 });
                var edit = Editor(); edit.Set(ImportDefaultChoice.IncludePreviews, false); var frozen = edit.Snapshot().Snapshot();
                edit.Set(ImportDefaultChoice.VideosOnly, false);
                var importer = new VerifiedImporter(new Operations(), path => UInt64.MaxValue, path => {});
                var result = importer.Run(source, source, destination, "modern-defaults-disposable", new ModernDefaultsGuard(), CancellationToken.None, message => {}, frozen);
                Check(result.Files == 1 && result.Receipt.Files.Single().SourceRelativePath == "clip.mp4" && result.Receipt.VideosOnly && !result.Receipt.IncludePreviews && result.Receipt.CleanLayout, "Import ignored frozen selection");
                Check(File.Exists(result.ReceiptPath) && result.RemovedOriginals == 0 && Directory.GetFiles(source).Length == 3 && File.ReadAllBytes(Path.Combine(source, "clip.mp4")).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Disposable originals or receipt changed");
            });
        }
        static string ModernDefaultsFixture() {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures", "modern-defaults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); return root;
        }
        sealed class ModernDefaultsGuard : IImportGuard {
            public int[] ProtectedDisks { get { return new[] { 770, 771 }; } }
            public void Validate() {}
        }
    }
}
