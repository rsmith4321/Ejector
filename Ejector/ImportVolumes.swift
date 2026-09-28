import Foundation
import DiskArbitration
import IOKit

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
    // A camera can expose separate whole disks for internal storage and an SD card.
    // Use the nearest USB device, never a shared hub, to recognize those siblings.
    static func usbDeviceID(_ url: URL) -> UInt64? {
        guard let session = DASessionCreate(nil),
              let disk = DADiskCreateFromVolumePath(nil, session, url as CFURL) else { return nil }
        var entry = DADiskCopyIOMedia(disk)
        while entry != 0 {
            if IOObjectConformsTo(entry, "IOUSBHostDevice") != 0 {
                var id: UInt64 = 0
                let status = IORegistryEntryGetRegistryEntryID(entry, &id)
                IOObjectRelease(entry)
                return status == KERN_SUCCESS ? id : nil
            }
            var parent: io_registry_entry_t = 0
            let status = IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent)
            IOObjectRelease(entry)
            entry = status == KERN_SUCCESS ? parent : 0
        }
        return nil
    }

    static func otherStorageSources(_ source: URL) -> [URL] {
        guard let device = usbDeviceID(source), let disk = physicalID(source) else { return [] }
        return mounted().filter {
            $0.standardizedFileURL != source.standardizedFileURL &&
                usbDeviceID($0) == device && physicalID($0) != disk
        }.sorted { $0.path < $1.path }
    }

    static func multiSourceEjectWarning(_ source: URL) -> String? {
        let others = otherStorageSources(source)
        guard !others.isEmpty else { return nil }
        return "\(source.lastPathComponent) shares its USB device with \(others.map(\.lastPathComponent).joined(separator: ", ")). Finish imports from every source, then eject all of this device’s volumes together in Finder. Easy Eject has left them connected."
    }

    static func mounted() -> [URL] {
        FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: [.volumeUUIDStringKey], options: [.skipHiddenVolumes]) ?? []
    }
}
