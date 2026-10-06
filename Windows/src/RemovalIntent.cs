using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    // Durable evidence for interrupted removal; this is never a completion receipt or eject authority.
    [DataContract] internal sealed class RemovalFileIdentity {
        [DataMember] internal uint VolumeSerial, IndexHigh, IndexLow, SizeHigh, SizeLow, Attributes, Links;
        [DataMember] internal int ModifiedHigh, ModifiedLow;
        internal static RemovalFileIdentity Capture(Native.FileInfo stamp) {
            return new RemovalFileIdentity { VolumeSerial = stamp.VolumeSerial, IndexHigh = stamp.IndexHigh, IndexLow = stamp.IndexLow,
                SizeHigh = stamp.SizeHigh, SizeLow = stamp.SizeLow, Attributes = stamp.Attributes, Links = stamp.Links,
                ModifiedHigh = stamp.Modified.dwHighDateTime, ModifiedLow = stamp.Modified.dwLowDateTime };
        }
        internal bool Matches(Native.FileInfo stamp) {
            return VolumeSerial == stamp.VolumeSerial && IndexHigh == stamp.IndexHigh && IndexLow == stamp.IndexLow &&
                SizeHigh == stamp.SizeHigh && SizeLow == stamp.SizeLow && Attributes == stamp.Attributes && Links == stamp.Links &&
                ModifiedHigh == stamp.Modified.dwHighDateTime && ModifiedLow == stamp.Modified.dwLowDateTime;
        }
    }
    [DataContract] internal sealed class RemovalIntentFile {
        [DataMember] internal string SourcePath, SavedRelativePath, SourceLabel, Sha256;
        [DataMember] internal long Bytes;
        [DataMember] internal bool Eligible, FromTrash, IsTrashRecord;
        [DataMember] internal string TrashRecordPath, TrashPayloadPath;
        [DataMember] internal RemovalFileIdentity SourceIdentity, SavedIdentity;
    }
    [DataContract] internal sealed class RemovalIntent {
        [DataMember] internal int Version = 1;
        [DataMember] internal string ImportId, CreatedUtc, SourceVolume, SourceRoot, SelectedFolder, DestinationRoot, BatchFolder;
        [DataMember] internal string WindowsTrashSid, MacTrashUid;
        [DataMember] internal DestinationIdentity SourceDevice, DestinationDevice;
        [DataMember] internal bool VideosOnly, IncludePreviews, CleanLayout, DeleteOriginals, RecoverTrash;
        [DataMember] internal bool IsCompletionReceipt = false;
        [DataMember] internal List<RemovalIntentFile> Files = new List<RemovalIntentFile>();
        internal FileStream Publish(string recordFolder, CancellationToken cancellation, Action validate, Action<string> audit) {
            string pending = Path.Combine(recordFolder, ".removal-intent-" + Guid.NewGuid().ToString("N") + ".partial");
            var stream = SafePaths.NewFile(pending); bool published = false;
            try {
                cancellation.ThrowIfCancellationRequested(); validate();
                new DataContractJsonSerializer(typeof(RemovalIntent)).WriteObject(stream, this); stream.Flush(true);
                stream.Position = 0; string hash = VerifiedImporter.Hash(stream, cancellation);
                cancellation.ThrowIfCancellationRequested(); validate();
                string target = Path.Combine(recordFolder, "removal-intent.json");
                Native.Rename(stream.SafeFileHandle, target); published = true; stream.Flush(true);
                if (!Native.FinalPath(stream.SafeFileHandle).Equals(target, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Removal evidence resolved to another path. Nothing was cleared.");
                stream.Position = 0;
                if (VerifiedImporter.Hash(stream, cancellation) != hash) throw new SafetyException("Removal evidence changed during publication. Nothing was cleared.");
                // Audit binding is flushed before any disposition. Keep this exclusive handle through completion.
                audit("Durable removal intent | SHA256 " + hash + " | " + target + " | no completion or eject authority");
                return stream;
            } catch {
                try { if (!published) Native.DeleteOnClose(stream.SafeFileHandle); } finally { stream.Dispose(); }
                throw;
            }
        }
    }
}
