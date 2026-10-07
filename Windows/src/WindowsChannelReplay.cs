using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal interface IWindowsChannelReplayStore {
        void Accept(ApprovedWindowsChannel channel, WindowsChannelOffer offer, CancellationToken cancel, Func<bool> stillAllowed);
    }
    internal interface IWindowsReplayProtection {
        byte[] Protect(byte[] bytes, byte[] entropy);
        byte[] Unprotect(byte[] bytes, byte[] entropy);
    }
    internal sealed class WindowsReplayProtection : IWindowsReplayProtection {
        public byte[] Protect(byte[] bytes, byte[] entropy) { return ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser); }
        public byte[] Unprotect(byte[] bytes, byte[] entropy) { return ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser); }
    }
    // A local replay restriction, never a cached offer or authority to acquire/install.
    // DPAPI authenticates user-bound state, not monotonic hardware storage: a user can
    // remove/restore whole old state. Normal restart protection assumes retained state.
    [DataContract] internal sealed class WindowsChannelReplayFloor {
        [DataMember(Name = "format")] internal readonly int Format = 1;
        [DataMember(Name = "policySha256")] internal readonly string PolicyHash;
        [DataMember(Name = "issuedUtc")] internal readonly long IssuedUtc;
        [DataMember(Name = "bodySha256")] internal readonly string BodyHash;
        [DataMember(Name = "version")] internal readonly string Version;
        [DataMember(Name = "releaseSha256")] internal readonly string ReleaseHash;
        internal WindowsChannelReplayFloor(string policy, long issued, string body, string version, string release) { PolicyHash = policy; IssuedUtc = issued; BodyHash = body; Version = version; ReleaseHash = release; }
        internal static string IdentityHash(params string[] fields) {
            using (var bytes = new MemoryStream()) { using (var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), true)) { foreach (string field in fields) writer.Write(field); writer.Flush(); } return WindowsSignerEvidence.Hash(bytes.ToArray()); }
        }
        internal static string Policy(ApprovedWindowsChannel channel) { return IdentityHash("EasyEject.Windows/channel-replay/policy/v1", channel.Metadata.AbsoluteUri, channel.Signature.AbsoluteUri, channel.Archives.AbsoluteUri, channel.Publisher.SubjectHash, channel.Publisher.IssuerCertificateHash); }
        internal static WindowsChannelReplayFloor From(ApprovedWindowsChannel channel, WindowsChannelOffer offer) {
            var r = offer.Release;
            return new WindowsChannelReplayFloor(Policy(channel), offer.IssuedUtc, offer.Authenticated.BodyHash, r.Version, IdentityHash("EasyEject.Windows/channel-replay/release/v1", r.Version, r.SourceRevision, r.ManifestHash, r.ManifestSignatureHash, r.CertificateHash, r.ArchiveName, r.ArchiveHash, r.ArchiveLength.ToString(System.Globalization.CultureInfo.InvariantCulture), r.ArchiveUrl.AbsoluteUri));
        }
        internal void RequireAtLeast(WindowsChannelReplayFloor previous) {
            if (previous == null) return;
            if (PolicyHash != previous.PolicyHash) throw new SafetyException("Windows replay state belongs to another approved channel policy. It is retained; no automatic reset or migration is allowed.");
            if (IssuedUtc < previous.IssuedUtc || IssuedUtc == previous.IssuedUtc && BodyHash != previous.BodyHash || new Version(Version).CompareTo(new Version(previous.Version)) < 0 || Version == previous.Version && ReleaseHash != previous.ReleaseHash) throw new SafetyException("The Windows channel rolled back or changed an already accepted release/version across saved replay state.");
        }
        internal bool Same(WindowsChannelReplayFloor other) { return other != null && PolicyHash == other.PolicyHash && IssuedUtc == other.IssuedUtc && BodyHash == other.BodyHash && Version == other.Version && ReleaseHash == other.ReleaseHash; }
        internal static WindowsChannelReplayFloor Parse(byte[] bytes, string policy) {
            if (bytes == null || bytes.Length == 0 || bytes.Length > WindowsChannelReplayStore.MaximumPlaintext) throw new SafetyException("Saved Windows replay payload is missing or oversized.");
            var fields = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson(bytes), new[] { "format", "policySha256", "issuedUtc", "bodySha256", "version", "releaseSha256" });
            long issued = WindowsReleaseManifest.Number(fields["issuedUtc"]); string storedPolicy = WindowsReleaseManifest.Text(fields["policySha256"]), body = WindowsReleaseManifest.Text(fields["bodySha256"]), version = WindowsReleaseManifest.Text(fields["version"]), release = WindowsReleaseManifest.Text(fields["releaseSha256"]);
            if (WindowsReleaseManifest.Number(fields["format"]) != 1 || !ImportHistory.ValidHash(storedPolicy) || storedPolicy != policy || issued > WindowsChannelOffer.MaximumEpoch || !ImportHistory.ValidHash(body) || !WindowsReleaseManifest.StableVersion(version) || !ImportHistory.ValidHash(release)) throw new SafetyException("Saved Windows replay state has unsupported format, policy, version, issue time or hashes. It is retained for review.");
            return new WindowsChannelReplayFloor(storedPolicy, issued, body, version, release);
        }
    }
    [DataContract] internal sealed class WindowsChannelReplayRecord {
        [DataMember(Name = "format")] internal readonly int Format = 1;
        [DataMember(Name = "protectedFloor")] internal readonly string ProtectedFloor;
        internal WindowsChannelReplayRecord(byte[] bytes) { ProtectedFloor = Convert.ToBase64String(bytes); }
    }
    internal sealed partial class WindowsChannelReplayStore : IWindowsChannelReplayStore {
        internal const string FileName = "windows-channel-replay.json";
        internal const int MaximumPlaintext = 1024, MaximumProtected = 16384, MaximumRecord = 32768;
        readonly string root; readonly IWindowsReplayProtection protection; readonly Action<string, string> checkpoint;
        readonly object sync = new object(); WindowsChannelReplayFloor seen; bool observed;
        internal sealed class Snapshot { internal WindowsChannelReplayFloor Floor; internal string Hash; internal Native.FileInfo Stamp; internal bool Exists; internal byte[] Encrypted; }
        internal WindowsChannelReplayStore(string root) : this(root, new WindowsReplayProtection(), null) { }
        internal WindowsChannelReplayStore(string root, IWindowsReplayProtection protection, Action<string, string> checkpoint = null) {
            this.root = SafePaths.Normalize(root); if (protection == null) throw new ArgumentNullException("protection"); this.protection = protection; this.checkpoint = checkpoint ?? ((stage, path) => {});
        }
        static byte[] Serialize(object value, Type type) { using (var output = new MemoryStream()) { new DataContractJsonSerializer(type).WriteObject(output, value); return output.ToArray(); } }
        static void Guard(WindowsChannelOffer offer, CancellationToken cancel, Func<bool> allowed) { cancel.ThrowIfCancellationRequested(); offer.RequireTime(); if (allowed != null && !allowed()) throw new SafetyException("Windows release activity/lifetime scope changed before persistent replay commit."); }
        static byte[] Entropy(string policy) { return Encoding.ASCII.GetBytes("EasyEject.Windows/channel-replay/protection/v1:" + policy); }
        WindowsChannelReplayFloor Unprotect(byte[] bytes, string policy) {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumProtected) throw new SafetyException("Saved Windows protected replay state is missing or oversized.");
            byte[] plain;
            try { plain = protection.Unprotect((byte[])bytes.Clone(), Entropy(policy)); } catch (CryptographicException) { throw new SafetyException("Windows user-bound replay state could not be authenticated. Retain it and review the current user/settings backup; no reset or plaintext fallback was performed."); }
            return WindowsChannelReplayFloor.Parse(plain == null ? null : (byte[])plain.Clone(), policy);
        }
        static byte[] ReadBytes(FileStream stream, string path, CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested(); var stamp = Native.Info(stream.SafeFileHandle);
            if ((stamp.Attributes & 0x1415) != 0 || stamp.Links != 1 || stream.Length <= 0 || stream.Length > MaximumRecord || !Native.FinalPath(stream.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Windows replay state is protected, redirected, aliased, empty or oversized. Existing state is retained.");
            WindowsPackageStreams.Require(path, stream.Length); stream.Position = 0; byte[] bytes = new byte[(int)stream.Length]; int offset = 0;
            while (offset < bytes.Length) { cancel.ThrowIfCancellationRequested(); int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new SafetyException("Windows replay state became truncated."); offset += count; }
            cancel.ThrowIfCancellationRequested(); if (!VerifiedImporter.SameStamp(stamp, Native.Info(stream.SafeFileHandle)) || Native.Info(stream.SafeFileHandle).Links != 1 || stream.Length != bytes.Length) throw new SafetyException("Windows replay state changed during its held read."); return bytes;
        }
        Snapshot Read(string path, string policy, CancellationToken cancel) {
            try { if ((File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Directory | FileAttributes.Offline | FileAttributes.ReparsePoint)) != 0) throw new SafetyException("Windows replay state is protected, redirected or is a directory. Existing state is retained."); } catch (FileNotFoundException) { return new Snapshot(); }
            using (var stream = SafePaths.ReadFile(path, 4096)) return ReadHeld(stream, path, policy, cancel);
        }
        Snapshot ReadHeld(FileStream stream, string path, string policy, CancellationToken cancel) {
            byte[] bytes = ReadBytes(stream, path, cancel); var fields = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson(bytes), new[] { "format", "protectedFloor" });
            if (WindowsReleaseManifest.Number(fields["format"]) != 1) throw new SafetyException("Saved Windows replay envelope format is unsupported."); string encoded = WindowsReleaseManifest.Text(fields["protectedFloor"]); byte[] encrypted;
            try { encrypted = Convert.FromBase64String(encoded); } catch (FormatException) { throw new SafetyException("Saved Windows replay envelope is not canonical bounded base64."); }
            if (encoded.Length == 0 || encoded.Length > (MaximumProtected + 2) / 3 * 4 || encrypted.Length == 0 || encrypted.Length > MaximumProtected || Convert.ToBase64String(encrypted) != encoded) throw new SafetyException("Saved Windows replay envelope is not canonical bounded base64.");
            var floor = Unprotect(encrypted, policy); cancel.ThrowIfCancellationRequested();
            // Protection can invoke native work; recheck file identity/stream aliases afterward.
            var stamp = Native.Info(stream.SafeFileHandle); if ((stamp.Attributes & 0x1415) != 0 || stamp.Links != 1 || stream.Length != bytes.Length || !Native.FinalPath(stream.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Windows replay state changed while authenticating it."); WindowsPackageStreams.Require(path, stream.Length);
            return new Snapshot { Floor = floor, Hash = WindowsSignerEvidence.Hash(bytes), Stamp = stamp, Exists = true, Encrypted = encrypted };
        }
        public void Accept(ApprovedWindowsChannel channel, WindowsChannelOffer offer, CancellationToken cancel, Func<bool> stillAllowed) {
            if (channel == null || offer == null) throw new ArgumentNullException("channel/offer"); cancel.ThrowIfCancellationRequested(); if (!offer.IsFor(channel)) throw new SafetyException("Persistent Windows replay acceptance requires the exact approved channel policy."); Guard(offer, cancel, stillAllowed); offer.Revalidate(cancel); Guard(offer, cancel, stillAllowed);
            lock (sync) using (var directories = ConfigurationWriter.CreateRoot(root)) using (var writer = Profiles.WriterGate(root)) {
                Guard(offer, cancel, stillAllowed); directories.Validate(); string path = Path.Combine(root, FileName), policy = WindowsChannelReplayFloor.Policy(channel);
                checkpoint("Before replay read", path); directories.Validate(); var current = Read(path, policy, cancel); directories.Validate(); Guard(offer, cancel, stillAllowed);
                if (observed && !current.Exists) throw new SafetyException("Previously observed Windows replay state disappeared. It is not silently recreated.");
                if (current.Exists) { current.Floor.RequireAtLeast(seen); seen = current.Floor; observed = true; }
                var next = WindowsChannelReplayFloor.From(channel, offer); next.RequireAtLeast(current.Floor); next.RequireAtLeast(seen);
                checkpoint("After replay read", path); directories.Validate(); Guard(offer, cancel, stillAllowed);
                if (next.Same(current.Floor)) {
                    using (var unchanged = SafePaths.ReadFile(path, 4096)) ValidatePrevious(current, unchanged, path, cancel);
                    directories.Validate(); Guard(offer, cancel, stillAllowed); return;
                }
                byte[] plain = Serialize(next, typeof(WindowsChannelReplayFloor)), encrypted;
                if (plain.Length > MaximumPlaintext) throw new SafetyException("Windows replay serialization exceeded its supported bound.");
                try { encrypted = protection.Protect((byte[])plain.Clone(), Entropy(policy)); } catch (CryptographicException) { throw new SafetyException("Windows user-bound replay state could not be protected. Existing state is retained; no plaintext fallback was performed."); }
                if (encrypted == null || encrypted.Length == 0 || encrypted.Length > MaximumProtected) throw new SafetyException("Windows replay protection returned an invalid bounded envelope."); encrypted = (byte[])encrypted.Clone();
                if (!next.Same(Unprotect(encrypted, policy))) throw new SafetyException("Windows replay protection returned another state. No publication occurred."); Guard(offer, cancel, stillAllowed); directories.Validate();
                ConfigurationWriter.Write(new WindowsChannelReplayRecord(encrypted), typeof(WindowsChannelReplayRecord), root, FileName,
                    (stage, file) => {
                        // Native publication already committed this floor. Retain it in
                        // the live store even if its final callback/check/flush fails.
                        if (stage == "After publication") { seen = next; observed = true; }
                        checkpoint(stage, file); directories.Validate(); if (stage != "After publication") Guard(offer, cancel, stillAllowed);
                    }, writer,
                    previous => {
                        Guard(offer, cancel, stillAllowed); directories.Validate();
                        ValidatePrevious(current, previous, path, cancel);
                    });
                seen = next; observed = true;
                // Publication has completed: a subsequent cancel/scope change cannot
                // undo or report an old durable floor. The discovery return guard owns it.
            }
        }
        static void ValidatePrevious(Snapshot current, FileStream previous, string path, CancellationToken cancel) {
            if (current.Exists != (previous != null) || previous != null && (!VerifiedImporter.SameStamp(current.Stamp, Native.Info(previous.SafeFileHandle)) || WindowsSignerEvidence.Hash(ReadBytes(previous, path, cancel)) != current.Hash)) throw new SafetyException("Windows replay state changed before its exclusive replacement. Existing state is retained.");
        }
    }
}
