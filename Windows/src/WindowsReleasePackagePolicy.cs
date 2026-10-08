using System;
using System.Threading;

namespace EasyEject {
    internal sealed class WindowsReleaseFilesReview {
        internal readonly string ArchivePath, Message;
        internal WindowsReleaseFilesReview(string archive, string message) { ArchivePath = archive; Message = message; }
    }
    // Typed factories retain ownership inside each explicit worker. The session
    // shares lifetime/delivery rules, never classic and modern package approval.
    internal sealed class WindowsReleasePackagePolicy {
        internal readonly bool IsModern;
        readonly Func<WindowsChannelOffer, string, string, bool, CancellationToken, Action, WindowsReleaseFilesReview> review;
        WindowsReleasePackagePolicy(bool modern, Func<WindowsChannelOffer, string, string, bool, CancellationToken, Action, WindowsReleaseFilesReview> review) { IsModern = modern; this.review = review; }
        internal WindowsReleaseFilesReview Review(WindowsChannelOffer offer, string archive, string folder, bool extracted, CancellationToken cancel, Action guard) { return review(offer, archive, folder, extracted, cancel, guard); }
        const string ArchiveMessage = "Selected ZIP matches the fresh signed release and exact archive hash. Extract it manually into a separate versioned folder, then select that folder for executable and package verification.";
        const string FinalMessage = "\r\n\r\nThis is a read-only result for these files at this time. Finish work and Quit before manually switching app folders. No installation or launch permission is retained.";
        internal static WindowsReleasePackagePolicy Classic(ApprovedWindowsChannel channel, Func<string, CancellationToken, WindowsArchiveReview> archive, Func<string, CancellationToken, WindowsPackageReview> package) {
            archive = archive ?? ((path, cancel) => new WindowsArchiveReview(path, channel.Publisher, new WindowsManifestTrust(), cancel));
            package = package ?? ((path, cancel) => new WindowsPackageReview(path, channel.Publisher, new WindowsAuthenticode(), new WindowsManifestTrust(), cancel));
            return new WindowsReleasePackagePolicy(false, (offer, path, folder, extracted, cancel, guard) => {
                using (var zip = archive(path, cancel)) {
                    guard(); offer.VerifyArchive(zip, cancel); guard();
                    if (!extracted) return new WindowsReleaseFilesReview(zip.Path, ArchiveMessage);
                    using (var contents = package(folder, cancel)) {
                        guard(); offer.VerifyExtracted(zip, contents, cancel); guard();
                        return new WindowsReleaseFilesReview(null, "Selected extracted folder matches the freshly verified ZIP, signed release, trusted executable publisher and all six package files.\r\nFolder: " + contents.Folder + FinalMessage);
                    }
                }
            });
        }
        internal static WindowsReleasePackagePolicy Modern(ApprovedWindowsChannel channel, Func<string, CancellationToken, ModernWindowsArchiveReview> archive, Func<string, CancellationToken, ModernWindowsApplicationPackageReview> package) {
            archive = archive ?? ((path, cancel) => new ModernWindowsArchiveReview(path, channel.Publisher, new WindowsManifestTrust(), cancel));
            package = package ?? ((path, cancel) => new ModernWindowsApplicationPackageReview(path, channel.Publisher, new WindowsAuthenticode(), new WindowsManifestTrust(), cancel));
            return new WindowsReleasePackagePolicy(true, (offer, path, folder, extracted, cancel, guard) => {
                using (var zip = archive(path, cancel)) {
                    guard(); offer.VerifyArchive(zip, cancel); guard();
                    if (!extracted) return new WindowsReleaseFilesReview(zip.Path, ArchiveMessage);
                    using (var contents = package(folder, cancel)) {
                        guard(); offer.VerifyExtracted(zip, contents, cancel); guard();
                        return new WindowsReleaseFilesReview(null, "Selected extracted folder matches the freshly verified ZIP, signed release, trusted apphost publisher, complete file inventory and application/launch declarations.\r\nFolder: " + contents.Folder + "\r\nComplete component/catalog and distribution acceptance is still required before installation." + FinalMessage);
                    }
                }
            });
        }
    }
}
