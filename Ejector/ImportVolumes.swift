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

    static func usbDeviceIsConnected(_ id: UInt64) -> Bool {
        let entry = IOServiceGetMatchingService(kIOMainPortDefault, IORegistryEntryIDMatching(id))
        guard entry != 0 else { return false }
        defer { IOObjectRelease(entry) }
        return IOObjectConformsTo(entry, "IOUSBHostDevice") != 0
    }

    static func mountedUSBVolumes(_ device: UInt64) -> [URL] {
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: [.volumeUUIDStringKey], options: []) ?? []
        return urls.filter { usbDeviceID($0) == device }.sorted { $0.path < $1.path }
    }

    static func otherStorageSources(_ source: URL) -> [URL] {
        guard let device = usbDeviceID(source), let disk = physicalID(source) else { return [] }
        return mountedUSBVolumes(device).filter {
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

/// Snapshot exactly the storage named in the confirmation. Never add a newly mounted volume.
nonisolated struct USBStorageUnmountPlan {
    struct Member {
        let url: URL
        let volumeID: String
        let diskID: String
    }
    let deviceID: UInt64
    let members: [Member]

    init(source: URL) throws {
        guard let device = ImportVolumes.usbDeviceID(source) else {
            throw ImportFailure("The camera connection could not be identified. Reconnect it and try again.")
        }
        deviceID = device
        members = try ImportVolumes.mountedUSBVolumes(device).map { url in
            guard let disk = ImportVolumes.physicalID(url) else {
                throw ImportFailure("A camera storage disk could not be identified. Nothing was ejected.")
            }
            return Member(url: url, volumeID: try ImportVolumes.identity(url), diskID: disk)
        }
        guard members.contains(where: { $0.url == source }), members.count > 1 else {
            throw ImportFailure("The camera storage changed. Check the connected sources and try again.")
        }
    }

    func validate(remaining: [URL]) throws {
        let expected = Set(remaining)
        guard ImportVolumes.usbDeviceIsConnected(deviceID),
              let allMounted = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: []),
              Set(allMounted.filter { ImportVolumes.usbDeviceID($0) == deviceID }) == expected,
              Set(allMounted).intersection(Set(members.map(\.url))) == expected else {
            throw ImportFailure("Camera storage disconnected, reconnected, or changed. Ejection stopped; check all sources before unplugging.")
        }
        for member in members where expected.contains(member.url) {
            guard try ImportVolumes.identity(member.url) == member.volumeID,
                  try ImportVolumes.root(member.url).standardizedFileURL == member.url.standardizedFileURL,
                  ImportVolumes.physicalID(member.url) == member.diskID else {
                throw ImportFailure("Camera storage changed. Ejection stopped; check all sources before unplugging.")
            }
        }
    }
}

/// Flush/unmount each volume without a hardware-eject command that could disconnect another LUN.
/// Report success only when validation confirms every approved volume is unmounted.
@MainActor enum SequentialVolumeUnmount {
    typealias Unmount = @MainActor (URL, @escaping @MainActor (Error?) -> Void) -> Void

    static func run(volumes: [URL], validate: @escaping ([URL]) throws -> Void,
                    unmount: @escaping Unmount = unmountVolume,
                    completion: @escaping (Error?) -> Void) {
        func next(_ index: Int) {
            do { try validate(Array(volumes.dropFirst(index))) }
            catch { completion(error); return }
            guard index < volumes.count else { completion(nil); return }
            unmount(volumes[index]) { error in
                if let error { completion(error); return }
                next(index + 1)
            }
        }
        next(0)
    }

    static func unmountVolume(_ url: URL, completion: @escaping @MainActor (Error?) -> Void) {
        FileManager.default.unmountVolume(at: url, options: [.withoutUI]) { error in
            Task { @MainActor in completion(error) }
        }
    }
}
