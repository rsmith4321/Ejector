#if APP_STORE
import Foundation

/// A physical eject affects every partition, including profiles not yet imported.
nonisolated enum StoreEjectPolicy {
    static func allowsAutomaticEject(sourceID: String, sourceDisk: String?,
                                     enrolledIDs: Set<String>,
                                     mounted: [(volumeID: String, diskID: String?)]) -> Bool {
        guard let sourceDisk,
              mounted.contains(where: { $0.volumeID == sourceID && $0.diskID == sourceDisk }) else { return false }
        return !mounted.contains { volume in
            volume.volumeID != sourceID && enrolledIDs.contains(volume.volumeID) &&
                (volume.diskID == nil || volume.diskID == sourceDisk)
        }
    }
}
#endif
