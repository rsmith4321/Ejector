import Foundation
import DiskArbitration

nonisolated enum ImportVolumes {
    static func identity(_ url: URL) throws -> String {
        var current = URL(fileURLWithPath: url.path)
        current.removeAllCachedResourceValues()
        guard let id = try current.resourceValues(forKeys: [.volumeUUIDStringKey]).volumeUUIDString else {
            throw ImportFailure("This volume does not provide a stable identity.")
        }
        return id.uppercased()
    }
    static func root(_ url: URL) throws -> URL {
        var current = URL(fileURLWithPath: url.path)
        current.removeAllCachedResourceValues()
        guard let root = try current.resourceValues(forKeys: [.volumeURLKey]).volume else {
            throw ImportFailure("Volume is not mounted.")
        }
        return root
    }
    static func physicalID(_ url: URL) -> String? {
        guard let session = DASessionCreate(nil), let disk = DADiskCreateFromVolumePath(nil, session, url as CFURL),
              let whole = DADiskCopyWholeDisk(disk), let name = DADiskGetBSDName(whole) else { return nil }
        return String(cString: name)
    }
    static func mounted() -> [URL] {
        FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: [.volumeUUIDStringKey], options: [.skipHiddenVolumes]) ?? []
    }
}
