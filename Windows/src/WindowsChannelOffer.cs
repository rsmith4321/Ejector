using System;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Supplied by approved application code, never by downloaded JSON or a candidate.
    // There is deliberately no configured production publisher or endpoint in this build.
    internal sealed class ApprovedWindowsChannel {
        internal readonly Uri Metadata, Signature, Archives; internal readonly ApprovedWindowsPublisher Publisher;
        internal ApprovedWindowsChannel(string metadata, string signature, string archives, ApprovedWindowsPublisher publisher) {
            if (publisher == null) throw new ArgumentNullException("publisher"); Publisher = publisher;
            Metadata = CanonicalHttps(metadata); Signature = CanonicalHttps(signature); Archives = CanonicalHttps(archives);
            if (!Metadata.AbsolutePath.EndsWith(".json", StringComparison.Ordinal) || Signature.AbsoluteUri != Metadata.AbsoluteUri + ".p7s" || !Archives.AbsolutePath.EndsWith("/", StringComparison.Ordinal)) throw new SafetyException("Approved Windows channel endpoints must use an exact JSON/signature pair and archive-directory base.");
        }
        static Uri CanonicalHttps(string text) {
            Uri uri;
            if (String.IsNullOrEmpty(text) || text.Length > 2048 || text.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c) || c > 127) || text.Contains("%") || text.Contains("\\") || !Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback || !uri.Host.Contains(".") || uri.AbsoluteUri != text)
                throw new SafetyException("Windows channel URLs require canonical approved public HTTPS addresses without credentials, aliases, queries or fragments.");
            return uri;
        }
        internal Uri Archive(string version, string name, string url) {
            if (!WindowsReleaseManifest.StableVersion(version) || name != "EasyEject.Windows-" + version + "-x64.zip") throw new SafetyException("The signed channel offered an unsupported Windows archive name/version.");
            Uri candidate = CanonicalHttps(url); if (candidate.AbsoluteUri != Archives.AbsoluteUri + version + "/" + name) throw new SafetyException("The signed Windows archive URL is outside the explicitly approved versioned archive location."); return candidate;
        }
        internal bool Same(ApprovedWindowsChannel other) { return other != null && Metadata.AbsoluteUri == other.Metadata.AbsoluteUri && Signature.AbsoluteUri == other.Signature.AbsoluteUri && Archives.AbsoluteUri == other.Archives.AbsoluteUri && Publisher.SubjectHash == other.Publisher.SubjectHash && Publisher.IssuerCertificateHash == other.Publisher.IssuerCertificateHash; }
    }
    internal enum WindowsReleaseComparison { NewerAvailable, SameStableVersion, ChannelOlder, UncomparableLocalVersion }
    internal sealed class WindowsChannelRelease {
        internal readonly string Version, SourceRevision, ManifestHash, ManifestSignatureHash, CertificateHash, ArchiveName, ArchiveHash;
        internal readonly Uri ArchiveUrl; internal readonly long ArchiveLength;
        internal WindowsChannelRelease(string version, string source, string manifest, string signature, string certificate, string name, string hash, long length, Uri url) {
            Version = version; SourceRevision = source; ManifestHash = manifest; ManifestSignatureHash = signature; CertificateHash = certificate; ArchiveName = name; ArchiveHash = hash; ArchiveLength = length; ArchiveUrl = url;
        }
        internal WindowsReleaseComparison Compare(string installed) {
            if (!WindowsReleaseManifest.StableVersion(installed)) return WindowsReleaseComparison.UncomparableLocalVersion;
            int comparison = new Version(Version).CompareTo(new Version(installed)); return comparison > 0 ? WindowsReleaseComparison.NewerAvailable : comparison < 0 ? WindowsReleaseComparison.ChannelOlder : WindowsReleaseComparison.SameStableVersion;
        }
        internal bool SameRelease(WindowsChannelRelease other) { return other != null && Version == other.Version && SourceRevision == other.SourceRevision && ManifestHash == other.ManifestHash && ManifestSignatureHash == other.ManifestSignatureHash && CertificateHash == other.CertificateHash && ArchiveName == other.ArchiveName && ArchiveHash == other.ArchiveHash && ArchiveLength == other.ArchiveLength && ArchiveUrl.AbsoluteUri == other.ArchiveUrl.AbsoluteUri; }
    }
    internal sealed class WindowsChannelOffer {
        internal const long MaximumLifetime = 7 * 24 * 60 * 60, MaximumClockSkew = 300, MaximumEpoch = 253402300799;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        readonly ApprovedWindowsChannel channel; readonly IWindowsManifestEnvelopeTrust trust; readonly Func<DateTime> clock;
        readonly byte[] body, signature; readonly string signatureHash;
        internal readonly WindowsAuthenticatedManifest Authenticated; internal readonly WindowsChannelRelease Release;
        internal readonly long IssuedUtc, ExpiresUtc;
        internal WindowsChannelOffer(byte[] bytes, byte[] encoded, ApprovedWindowsChannel channel, IWindowsManifestEnvelopeTrust trust, Func<DateTime> clock, CancellationToken cancel) {
            if (channel == null) throw new SafetyException("No approved Windows channel is configured. A release check is unavailable.");
            if (trust == null || clock == null) throw new ArgumentNullException("trust/clock"); this.channel = channel; this.trust = trust; this.clock = clock; cancel.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length == 0 || bytes.Length > WindowsManifestTrust.MaximumManifest || encoded == null || encoded.Length == 0 || encoded.Length > WindowsManifestTrust.MaximumSignature) throw new SafetyException("Signed Windows channel metadata/signature exceeds its supported bounds.");
            body = (byte[])bytes.Clone(); signature = (byte[])encoded.Clone(); signatureHash = WindowsSignerEvidence.Hash(signature); Authenticated = Authenticate(cancel);
            var root = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson(Authenticated.CopyBytes()), new[] { "format", "kind", "application", "platform", "channel", "issuedUtc", "expiresUtc", "release" });
            if (WindowsReleaseManifest.Number(root["format"]) != 1 || WindowsReleaseManifest.Text(root["kind"]) != "windows-channel" || WindowsReleaseManifest.Text(root["application"]) != WindowsReleaseManifest.Application || WindowsReleaseManifest.Text(root["platform"]) != WindowsReleaseManifest.Platform || WindowsReleaseManifest.Text(root["channel"]) != "stable") throw new SafetyException("This is not the approved Easy Eject Windows x64 stable channel format.");
            IssuedUtc = WindowsReleaseManifest.Number(root["issuedUtc"]); ExpiresUtc = WindowsReleaseManifest.Number(root["expiresUtc"]); RequireTime();
            var release = WindowsReleaseManifest.Fields(root["release"], new[] { "version", "sourceRevision", "manifestSha256", "manifestSignatureSha256", "certificateSha256", "archive" });
            string version = WindowsReleaseManifest.Text(release["version"]), source = WindowsReleaseManifest.Text(release["sourceRevision"]), manifest = WindowsReleaseManifest.Text(release["manifestSha256"]), sig = WindowsReleaseManifest.Text(release["manifestSignatureSha256"]), certificate = WindowsReleaseManifest.Text(release["certificateSha256"]);
            if (!WindowsReleaseManifest.StableVersion(version) || source.Length != 40 || source.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) || !ImportHistory.ValidHash(manifest) || !ImportHistory.ValidHash(sig) || !ImportHistory.ValidHash(certificate)) throw new SafetyException("The signed Windows channel release identity/hashes are invalid.");
            var archive = WindowsReleaseManifest.Fields(release["archive"], new[] { "name", "url", "length", "sha256" }); string name = WindowsReleaseManifest.Text(archive["name"]), url = WindowsReleaseManifest.Text(archive["url"]), hash = WindowsReleaseManifest.Text(archive["sha256"]); long length = WindowsReleaseManifest.Number(archive["length"]);
            if (length < 22 || length > WindowsArchiveReview.MaximumArchive || !ImportHistory.ValidHash(hash)) throw new SafetyException("The signed Windows channel archive bounds/hash are invalid.");
            Release = new WindowsChannelRelease(version, source, manifest, sig, certificate, name, hash, length, channel.Archive(version, name, url)); cancel.ThrowIfCancellationRequested(); RequireTime();
        }
        WindowsAuthenticatedManifest Authenticate(CancellationToken cancel) {
            var result = trust.VerifyEnvelope((byte[])body.Clone(), (byte[])signature.Clone(), channel.Publisher, cancel); cancel.ThrowIfCancellationRequested();
            if (result == null || result.BodyHash != WindowsSignerEvidence.Hash(body) || result.SignatureHash != signatureHash) throw new SafetyException("Windows channel trust returned a different authenticated snapshot."); channel.Publisher.Require(result.Signer); return result;
        }
        internal void RequireTime() {
            DateTime now = clock(); if (now.Kind != DateTimeKind.Utc || now < Epoch) throw new SafetyException("A Windows channel check requires a valid UTC clock."); long seconds = (now.Ticks - Epoch.Ticks) / TimeSpan.TicksPerSecond;
            if (IssuedUtc > MaximumEpoch || ExpiresUtc > MaximumEpoch || ExpiresUtc <= IssuedUtc || ExpiresUtc - IssuedUtc > MaximumLifetime || IssuedUtc > seconds + MaximumClockSkew || ExpiresUtc <= seconds) throw new SafetyException("The signed Windows channel is expired, future-dated or outside the permitted lifetime.");
        }
        internal bool IsFor(ApprovedWindowsChannel approved) { return channel.Same(approved); }
        internal void Revalidate(CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested(); RequireTime(); var fresh = Authenticate(cancel); if (fresh.Signer.CertificateHash != Authenticated.Signer.CertificateHash || fresh.Signer.DigestOid != Authenticated.Signer.DigestOid) throw new SafetyException("The signed Windows channel signer changed during review."); RequireTime();
        }
        internal void VerifyArchive(WindowsArchiveReview archive, CancellationToken cancel) {
            if (archive == null) throw new ArgumentNullException("archive"); Revalidate(cancel); archive.Revalidate(cancel); channel.Publisher.Require(archive.Authenticated.Signer);
            if (archive.Hash != Release.ArchiveHash || archive.Length != Release.ArchiveLength || archive.Release.Version != Release.Version || archive.Release.SourceRevision != Release.SourceRevision || archive.Authenticated.BodyHash != Release.ManifestHash || archive.Authenticated.SignatureHash != Release.ManifestSignatureHash || archive.Authenticated.Signer.CertificateHash != Release.CertificateHash) throw new SafetyException("The held Windows ZIP does not match the signed channel's exact archive, manifest, release and signer.");
            Revalidate(cancel);
        }
        internal void VerifyExtracted(WindowsArchiveReview archive, WindowsPackageReview package, CancellationToken cancel) { VerifyArchive(archive, cancel); archive.VerifyExtractedPackage(package, cancel); Revalidate(cancel); }
    }
    // Process-only replay/high-water protection. Durable restart protection is separate
    // remaining configuration work; no discovery result is installed or launched here.
    internal sealed class WindowsChannelHighWater {
        readonly ApprovedWindowsChannel channel; readonly object sync = new object(); WindowsChannelOffer highest;
        internal WindowsChannelHighWater(ApprovedWindowsChannel channel) { if (channel == null) throw new ArgumentNullException("channel"); this.channel = channel; }
        internal void Accept(WindowsChannelOffer offer, CancellationToken cancel, Func<bool> stillAllowed = null) {
            if (offer == null) throw new ArgumentNullException("offer"); cancel.ThrowIfCancellationRequested(); if (!offer.IsFor(channel)) throw new SafetyException("A Windows discovery result belongs to a different approved channel policy."); offer.Revalidate(cancel);
            lock (sync) {
                offer.RequireTime(); cancel.ThrowIfCancellationRequested();
                if (highest != null) {
                    if (offer.IssuedUtc < highest.IssuedUtc || offer.IssuedUtc == highest.IssuedUtc && offer.Authenticated.BodyHash != highest.Authenticated.BodyHash || new Version(offer.Release.Version).CompareTo(new Version(highest.Release.Version)) < 0 || offer.Release.Version == highest.Release.Version && !offer.Release.SameRelease(highest.Release)) throw new SafetyException("The signed Windows channel rolled back or changed an already observed release/version.");
                }
                if (stillAllowed != null && !stillAllowed()) throw new SafetyException("The Windows release check's activity/lifetime scope changed before discovery commit.");
                cancel.ThrowIfCancellationRequested(); highest = offer;
            }
        }
    }
}
