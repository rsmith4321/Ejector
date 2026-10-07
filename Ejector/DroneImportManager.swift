import SwiftUI
import Combine
import DiskArbitration
import UserNotifications

@MainActor final class DroneImportManager: ObservableObject {
    static let shared = DroneImportManager()
    @Published var profiles: [DroneProfile] = []
    @Published var progress = ImportProgress(phase: "Ready")
    @Published var busy = false
    @Published var message = "Connect a camera, memory card, or FPV device to set up automatic imports."
    @Published var activeName = ""
    @Published var lastFolder: URL?
    @Published var hasError = false {
        didSet { isIssueDismissed = false }
    }
    @Published private(set) var isIssueDismissed = false
    var needsAttention: Bool { hasError && !isIssueDismissed }
    @Published var volumes: [URL] = []
    @Published private(set) var pendingEjects: [ImportCompletion] = []
    #if !APP_STORE
    @Published private(set) var completedImports: [QuickShareImport] = []
    var quickShareImports: [QuickShareImport] {
        completedImports.filter { quickShareProfile(for: $0.id) != nil }
    }
    func quickShareProfile(for id: String) -> DroneProfile? {
        guard let batch = completedImports.first(where: { $0.id == id }) else { return nil }
        return profiles.first { $0.id == batch.profileID && $0.sharingPreset != nil }
    }
    #endif
    private var completions = ImportCompletionRegistry()
    private var connections: [URL: UUID] = [:]
    private var attempted = Set<String>()
    private var waiting = Set<String>()
    private var mountedIDs = Set<String>()
    private var completedEjectID: String?
    private var sourceRoot: URL?
    private var destinationRoot: URL?
    private var protectedPhysicalIDs = Set<String>()
    private var manualOperations = Set<String>()
    private var cancellation: ImportCancellation?
    private var observations = Set<AnyCancellable>()
    private let queue = DispatchQueue(label: "com.ryansmithphotography.Ejector.import", qos: .utility)
    private let support: URL

    init(supportDirectory: URL? = nil) {
        let supportName = Bundle.main.bundleIdentifier?.hasSuffix(".preview") == true ? "Easy Eject Preview" : "Easy Eject"
        support = supportDirectory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent(supportName)
        do {
            try FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
            let file = support.appendingPathComponent("drone-profiles.json")
            if FileManager.default.fileExists(atPath: file.path) {
                profiles = try JSONDecoder().decode([DroneProfile].self, from: Data(contentsOf: file))
            }
        } catch { message = "Could not load import profiles: \(error.localizedDescription)"; hasError = true }
        #if !APP_STORE
        do { completedImports = try QuickShareImport.load(from: support.appendingPathComponent("completed-imports.json")) }
        catch { message = "Could not load completed imports: \(error.localizedDescription)"; hasError = true }
        #endif
        let center = NSWorkspace.shared.notificationCenter
        // Invalidate synchronously, before the debounced device-list refresh. BSD names and UUIDs
        // can both be reused when the same card is reconnected.
        center.publisher(for: NSWorkspace.didUnmountNotification)
            .merge(with: center.publisher(for: NSWorkspace.didMountNotification))
            .merge(with: center.publisher(for: NSWorkspace.didRenameVolumeNotification))
            .sink { [weak self] event in
                guard let self, let url = event.userInfo?[NSWorkspace.volumeURLUserInfoKey] as? URL else { return }
                self.connections[url] = UUID()
                self.clearCompletions(for: url)
            }.store(in: &observations)
        center.publisher(for: NSWorkspace.didMountNotification)
            .merge(with: center.publisher(for: NSWorkspace.didUnmountNotification))
            .merge(with: center.publisher(for: NSWorkspace.didRenameVolumeNotification))
            .debounce(for: .milliseconds(700), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                guard let self else { return }
                self.attempted.subtract(self.waiting); self.waiting.removeAll(); self.refresh()
            }.store(in: &observations)
        // Monitoring exists from app launch, even when the menu has never been opened.
        DispatchQueue.main.async { self.refresh() }
    }

    var menuTitle: String {
        if busy {
            if progress.phase == "Creating sharing copies" { return "Sharing \(Int(progress.fraction * 100))%" }
            if ["Importing", "Checking", "Verifying copy", "Verifying", "Imported"].contains(progress.phase) {
                return "Importing \(Int(progress.fraction * 100))%"
            }
            return progress.phase
        }
        return ""
    }

    /// Acknowledging a notice never retries an import or changes its saved options.
    /// Keep the explanation visible until the next operation replaces it.
    func dismissIssue() {
        guard !busy, hasError else { return }
        isIssueDismissed = true
    }

    func save(_ profile: DroneProfile) {
        guard !busy else { return }
        var updated = profiles
        if let i = updated.firstIndex(where: { $0.id == profile.id }) { updated[i] = profile }
        else { updated.append(profile) }
        persist(updated)
    }
    func remove(_ id: String) { guard !busy else { return }; persist(profiles.filter { $0.id != id }) }
    private func persist(_ updated: [DroneProfile]) {
        do {
            try JSONEncoder().encode(updated).write(to: support.appendingPathComponent("drone-profiles.json"), options: .atomic)
            profiles = updated
            // Saving settings never imports immediately. Use Import now or the next connection.
        } catch { message = "Cannot save settings: \(error.localizedDescription)"; hasError = true }
    }
    func cancel() { cancellation?.cancel(); message = "Stopping safely. Completed files are already saved." }
    func openFolder() { if let lastFolder { NSWorkspace.shared.open(lastFolder) } }
    func openLog() { NSWorkspace.shared.open(support.appendingPathComponent("imports.log")) }

    func blocksEject(_ url: URL) -> Bool {
        if busy {
            if url == sourceRoot || url == destinationRoot { return true }
            if let device = ImportVolumes.usbDeviceID(url),
               [sourceRoot, destinationRoot].compactMap({ $0 }).contains(where: { ImportVolumes.usbDeviceID($0) == device }) { return true }
            if let key = ImportVolumes.physicalID(url), protectedPhysicalIDs.contains(key) { return true }
        }
        return false
    }
    func reserveManualEject(_ url: URL) -> Bool {
        let key = ImportVolumes.physicalID(url) ?? url.path
        guard !blocksEject(url), !manualOperations.contains(key) else { return false }
        manualOperations.insert(key); return true
    }
    func recordOperationFailure(_ error: Error) {
        guard !busy else { LogManager.shared.log(error.localizedDescription); return }
        hasError = true; message = error.localizedDescription
        progress = ImportProgress(phase: "Needs attention")
    }
    func recordManualEject(_ volume: URL, error: Error?) {
        guard !busy else { return }
        hasError = error != nil
        message = error.map { "Could not eject \(volume.lastPathComponent): \($0.localizedDescription)" } ?? "\(volume.lastPathComponent): safe to unplug."
        // The volume may already be gone; reset success on the next mount event as well.
        progress = ImportProgress(phase: error == nil ? "Ejected" : "Needs attention")
    }
    func finishManualEject(_ key: String) { manualOperations.remove(key); refresh() }

    func refresh() {
        volumes = ImportVolumes.mounted().filter {
            guard $0.path.hasPrefix("/Volumes/"), let v = try? $0.resourceValues(forKeys: [.volumeIsInternalKey, .volumeIsRemovableKey, .volumeIsEjectableKey]) else { return false }
            return v.volumeIsInternal != true || v.volumeIsRemovable == true || v.volumeIsEjectable == true
        }
        let ids = Set(volumes.compactMap { try? ImportVolumes.identity($0) })
        if !busy, (completedEjectID.map { ids.contains($0) } ?? false) || (!ids.subtracting(mountedIDs).isEmpty && progress.phase == "Ejected") {
            self.completedEjectID = nil
            message = "Device connected again. Eject it before unplugging."
            progress = ImportProgress(phase: "Ready")
        }
        attempted.subtract(mountedIDs.subtracting(ids)); mountedIDs = ids
        guard !busy else { return }
        for profile in profiles where profile.resolved(using: ImportDefaults()).enabled && !attempted.contains(profile.id) {
            if let source = volumes.first(where: { (try? ImportVolumes.identity($0)) == profile.id }) {
                start(profile, source: source)
                if busy { break }
            }
        }
    }

    func importNow(_ profile: DroneProfile) {
        guard !busy else { return }
        guard let source = volumes.first(where: { (try? ImportVolumes.identity($0)) == profile.id }) else {
            message = "Connect \(profile.name) first."; hasError = true
            progress = ImportProgress(phase: "Needs attention"); return
        }
        start(profile, source: source)
    }

    private func start(_ storedProfile: DroneProfile, source: URL) {
        // Freeze defaults for the whole operation, including its final eject choice.
        let profile = storedProfile.resolved(using: ImportDefaults())
        clearCompletions(for: source)
        let connection = connections[source] ?? UUID()
        connections[source] = connection
        attempted.insert(profile.id)
        #if !APP_STORE
        let legacy = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/O4 Media Importer/enabled")
        if FileManager.default.fileExists(atPath: legacy.path) {
            message = "The standalone O4 importer is still enabled. Pause it before starting imports in Easy Eject."; hasError = true
            progress = ImportProgress(phase: "Needs attention"); return
        }
        #endif
        do {
            #if APP_STORE
            guard let bookmark = profile.sourceBookmark else { throw ImportFailure("Choose the media folder again to grant access.") }
            // Trash is outside the selected media folder. Never broaden access silently.
            let trashAccess = profile.recoverTrash ? try CardAccess.shared.open(source) : nil
            let sourceAccess = try ScopedFolder(bookmark)
            let destinationAccess = try ScopedFolder(profile.destinationBookmark)
            let media = sourceAccess.url
            let destination = destinationAccess.url
            guard try ImportVolumes.identity(media) == profile.id,
                  try ImportVolumes.root(media).standardizedFileURL == source.standardizedFileURL,
                  MediaImportEngine.isWithin(media, source) else {
                throw ImportFailure("The authorized media folder no longer belongs to this device. Choose it again.")
            }
            if sourceAccess.refreshedBookmark != nil || destinationAccess.refreshedBookmark != nil {
                var updated = storedProfile
                updated.sourceBookmark = sourceAccess.refreshedBookmark ?? bookmark
                updated.destinationBookmark = destinationAccess.refreshedBookmark ?? profile.destinationBookmark
                updated.mediaPath = String(media.path.dropFirst(source.path.count + 1))
                updated.destinationLabel = destination.path
                save(updated)
                guard profiles.contains(updated) else { throw ImportFailure("Cannot save refreshed folder permissions. Import stopped.") }
            }
            #else
            var stale = false
            let destination = try URL(resolvingBookmarkData: profile.destinationBookmark, options: [.withoutUI, .withoutMounting], bookmarkDataIsStale: &stale)
            guard !stale else { throw ImportFailure("Choose the destination folder again to refresh its permission.") }
            let accessed = destination.startAccessingSecurityScopedResource()
            #endif
            do {
                let destinationVolume = try ImportVolumes.root(destination)
                guard try ImportVolumes.identity(destination) == profile.destinationVolumeID else { throw ImportFailure("The selected destination drive is unavailable or changed.") }
                guard try ImportVolumes.identity(source) != profile.destinationVolumeID,
                      ImportVolumes.physicalID(source) != ImportVolumes.physicalID(destinationVolume) else {
                    throw ImportFailure("Choose a destination on a different disk from the device.")
                }
                #if !APP_STORE
                let media = source.appendingPathComponent(profile.mediaPath).standardizedFileURL
                guard MediaImportEngine.isWithin(media, source),
                      !profile.mediaPath.split(separator: "/").contains(".."), !profile.mediaPath.hasPrefix("/") else {
                    throw ImportFailure("Choose a media folder inside the device.")
                }
                #endif
                let originalSourceDisk = ImportVolumes.physicalID(source)
                let diskKeys = Set([originalSourceDisk ?? source.path, ImportVolumes.physicalID(destinationVolume) ?? destinationVolume.path])
                guard manualOperations.isDisjoint(with: diskKeys) else { throw ImportFailure("A disk is being ejected. Reconnect it before importing.") }
                let releaseAccess: @Sendable () -> Void = {
                    #if APP_STORE
                    sourceAccess.close(); destinationAccess.close(); trashAccess?.close()
                    #else
                    if accessed { destination.stopAccessingSecurityScopedResource() }
                    #endif
                }
                sourceRoot = source; destinationRoot = destinationVolume; protectedPhysicalIDs = diskKeys
                busy = true; hasError = false; activeName = profile.name; message = "Preparing import"; progress = ImportProgress(phase: "Scanning")
                let token = ImportCancellation(); cancellation = token
                let logURL = support.appendingPathComponent("imports.log")
                queue.async {
                    let validate: () throws -> Void = {
                        try token.check()
                        guard try ImportVolumes.identity(source) == profile.id,
                              try ImportVolumes.root(source).standardizedFileURL == source.standardizedFileURL,
                              try ImportVolumes.identity(destination) == profile.destinationVolumeID,
                              try ImportVolumes.root(destination).standardizedFileURL == destinationVolume.standardizedFileURL else {
                            throw ImportFailure("A source or destination volume disconnected or changed. Remaining originals were kept.")
                        }
                    }
                    let audit: (String) throws -> Void = { line in
                        let text = "\(ISO8601DateFormatter().string(from: Date())) \(line)\n"
                        if !FileManager.default.fileExists(atPath: logURL.path) { FileManager.default.createFile(atPath: logURL.path, contents: nil) }
                        let h = try FileHandle(forWritingTo: logURL); defer { try? h.close() }
                        try h.seekToEnd(); try h.write(contentsOf: Data(text.utf8)); try h.synchronize()
                    }
                    var lastReport = Date.distantPast
                    var lastPhase = ""
                    let engine = MediaImportEngine(source: source, mediaFolder: media, destination: destination,
                        deleteOriginals: profile.deleteOriginals, recoverTrash: profile.recoverTrash, cancellation: token,
                        validate: validate, report: { value in
                            // A native UI update at most ten times per second keeps large transfers lightweight.
                            if value.phase != lastPhase || Date().timeIntervalSince(lastReport) >= 0.1 || value.completed == value.total {
                                lastReport = Date(); lastPhase = value.phase
                                DispatchQueue.main.async { self.progress = value }
                            }
                        }, audit: audit, videosOnly: profile.videosOnly ?? false, cleanLayout: profile.cleanLayout ?? false, sourceIdentifier: profile.id, includePreviews: profile.includePreviews ?? true)
                    do {
                        let result = try engine.run()
                        try validate()
                        try audit("Completed \(result.files) files, \(result.bytes) bytes")
                        DispatchQueue.main.async {
                            self.lastFolder = result.files > 0 ? result.folder : destination
                            let importNote = (result.note.isEmpty ? "" : " " + result.note)
                            let completion = result.hasSelectedMedia
                                ? "\(profile.name): \(result.files) files imported and verified."
                                : "\(profile.name): no media found matching your import options."
                            let completionID = UUID().uuidString
                            let entry = ImportCompletion(id: completionID, source: source, volumeID: profile.id,
                                physicalID: originalSourceDisk, connection: connection, deviceName: profile.name,
                                hasMedia: result.hasSelectedMedia, folder: self.lastFolder!,
                                destinationBookmark: profile.destinationBookmark, destinationVolumeID: profile.destinationVolumeID)
                            #if !APP_STORE
                            if !result.verifiedMedia.isEmpty {
                                let batch = QuickShareImport(id: completionID, profileID: profile.id, deviceName: profile.name,
                                    date: Date(), folder: result.folder, destinationBookmark: profile.destinationBookmark,
                                    destinationVolumeID: profile.destinationVolumeID, originals: result.verifiedMedia)
                                self.completedImports.insert(batch, at: 0)
                                self.completedImports = Array(self.completedImports.prefix(20))
                                do { try QuickShareImport.save(self.completedImports, to: self.support.appendingPathComponent("completed-imports.json")) }
                                catch { self.logEjectEvent("Import verified; could not save Quick Share history: \(error.localizedDescription)") }
                            }
                            #endif
                            releaseAccess()
                            guard profile.autoEject, self.connections[source] == connection else {
                                self.finish(error: nil, message: completion + " Device remains connected." + importNote)
                                return
                            }
                            self.removeNotifications(self.completions.insert(entry))
                            self.updatePendingEjects()
                            // Release import reservations before presenting the eject choice.
                            self.finish(error: nil, message: completion + " Device remains connected." + importNote,
                                        notify: false, refreshDevices: false)
                            self.showEjectPrompt(entry)
                            self.refresh()

                        }
                    } catch {
                        try? audit("Stopped: \(error.localizedDescription)")
                        DispatchQueue.main.async {
                            releaseAccess()
                            self.finish(error: error, message: error.localizedDescription)
                        }
                    }
                }
            } catch {
                #if !APP_STORE
                if accessed { destination.stopAccessingSecurityScopedResource() }
                #endif
                throw error
            }
        } catch {
            waiting.insert(profile.id); hasError = true; activeName = profile.name
            progress = ImportProgress(phase: "Waiting"); message = error.localizedDescription
        }
    }

    private func updatePendingEjects() {
        pendingEjects = completions.entries.values.sorted { $0.deviceName.localizedStandardCompare($1.deviceName) == .orderedAscending }
    }

    private func removeNotifications(_ ids: [String]) {
        guard !ids.isEmpty else { return }
        let center = UNUserNotificationCenter.current()
        center.removePendingNotificationRequests(withIdentifiers: ids)
        center.removeDeliveredNotifications(withIdentifiers: ids)
    }

    private func clearCompletions(for source: URL) {
        removeNotifications(completions.invalidate(source: source))
        updatePendingEjects()
    }

    private func showEjectPrompt(_ entry: ImportCompletion) {
        guard completions.entries[entry.id] != nil else { return }
        #if !APP_STORE
        let shareProfile = quickShareProfile(for: entry.id)
        let offersQuickShare = shareProfile != nil
        let gyroflowFirst = shareProfile?.sharingCamera == .djiO4Pro
        #else
        let offersQuickShare = false
        let gyroflowFirst = false
        #endif
        if !ImportVolumes.otherStorageSources(entry.source).isEmpty {
            do {
                let plan = try USBStorageUnmountPlan(source: entry.source)
                var expectedConnections: [URL: UUID] = [:]
                for member in plan.members {
                    let token = connections[member.url] ?? UUID()
                    connections[member.url] = token
                    expectedConnections[member.url] = token
                }
                let alert = ImportEjectPrompt.make(hasMedia: entry.hasMedia,
                    deviceName: entry.deviceName, sourceName: entry.source.lastPathComponent,
                    storageSources: plan.members.map { $0.url.lastPathComponent }, quickShare: offersQuickShare, gyroflowFirst: gyroflowFirst)
                NSApp.activate()
                let choice = alert.runModal()
                if choice == .alertSecondButtonReturn {
                    ejectCameraStorage(entry, plan: plan, expectedConnections: expectedConnections)
                }
                #if !APP_STORE
                if choice == .alertThirdButtonReturn {
                    if gyroflowFirst { openImportedFolder(entry.id) }
                    else { quickShareCompletedImport(entry.id, returnToEject: true) }
                }
                #endif
            } catch { recordOperationFailure(error) }
            return
        }
        let alert = ImportEjectPrompt.make(hasMedia: entry.hasMedia, deviceName: entry.deviceName, quickShare: offersQuickShare, gyroflowFirst: gyroflowFirst)
        NSApp.activate()
        let choice = alert.runModal()
        if choice == .alertSecondButtonReturn { ejectCompletedImport(entry.id) }
        #if !APP_STORE
        if choice == .alertThirdButtonReturn {
            if gyroflowFirst { openImportedFolder(entry.id) }
            else { quickShareCompletedImport(entry.id, returnToEject: true) }
        }
        #endif
    }

    #if !APP_STORE
    func openImportedFolder(_ id: String) {
        guard !busy, let batch = completedImports.first(where: { $0.id == id }),
              quickShareProfile(for: id)?.sharingCamera == .djiO4Pro else { return }
        guard FileManager.default.fileExists(atPath: batch.folder.path) else {
            message = "The imported folder is unavailable. Connect the destination drive and try again."
            return
        }
        guard NSWorkspace.shared.open(batch.folder) else {
            message = "Could not open the imported folder."
            return
        }
        message = "Import folder opened. Stabilize O4 Pro clips in Gyroflow before using Quick Share to apply the DJI LUT."
    }

    func quickShareCompletedImport(_ id: String, returnToEject: Bool = false) {
        guard !busy, let batch = completedImports.first(where: { $0.id == id }),
              let profile = quickShareProfile(for: id) else { return }
        NSApp.activate()
        let camera = profile.sharingCamera ?? .luna
        if camera == .djiO4Pro {
            let masters = batch.originals.map(\.url).filter {
                $0.deletingPathExtension().lastPathComponent.range(of: #"^DJI_\d{14}_\d{4}_D$"#, options: .regularExpression) != nil
            }
            let missing = masters.filter { master in
                let output = master.deletingLastPathComponent().appendingPathComponent(master.deletingPathExtension().lastPathComponent + "_stabilized.mp4")
                return !FileManager.default.fileExists(atPath: output.path)
            }
            if !missing.isEmpty {
                message = "Finish these O4 Pro videos in Gyroflow first: \(missing.map(\.lastPathComponent).joined(separator: ", ")). Export beside the originals with the default _stabilized.mp4 name, then choose Quick Share."
                return
            }
        }
        guard let choice = QuickSharePrompt.choose(deviceName: batch.deviceName,
            preset: profile.sharingPreset ?? .hd, color: profile.sharingColor ?? camera.logColor, camera: camera) else { return }
        createQuickShare(batch, preset: choice.preset, color: choice.color, returnToEject: returnToEject)
    }

    /// Called only after a manual Quick Share choice. Never invoked by start/refresh/importNow.
    func createQuickShare(_ batch: QuickShareImport, preset: EasySharePreset, color: EasyShareColor,
                          returnToEject: Bool = false) {
        guard !busy, completedImports.contains(batch), let profile = quickShareProfile(for: batch.id) else { return }
        let camera = profile.sharingCamera ?? .luna
        do {
            var stale = false
            let destination = try URL(resolvingBookmarkData: batch.destinationBookmark,
                options: [.withoutUI, .withoutMounting], bookmarkDataIsStale: &stale)
            guard !stale else { throw ImportFailure("The saved import permission expired. Choose the destination again and reimport to restore Quick Share access.") }
            let accessed = destination.startAccessingSecurityScopedResource()
            do {
                try MediaImportEngine.checkPath(batch.folder)
                let volume = try ImportVolumes.root(destination)
                let physical = ImportVolumes.physicalID(volume)
                let key = physical ?? volume.path
                guard MediaImportEngine.isWithin(batch.folder, destination),
                      try ImportVolumes.identity(destination) == batch.destinationVolumeID,
                      try ImportVolumes.identity(batch.folder) == batch.destinationVolumeID else {
                    throw ImportFailure("The saved import destination is disconnected or changed.")
                }
                guard !manualOperations.contains(key) else { throw ImportFailure("The import destination is being ejected. Reconnect it before making Quick Share copies.") }
                let tools = try EasyShareTools.installed()
                let stabilized: [URL] = camera == .djiO4Pro ? batch.originals.map(\.url).compactMap { master in
                    let output = master.deletingLastPathComponent().appendingPathComponent(master.deletingPathExtension().lastPathComponent + "_stabilized.mp4")
                    return FileManager.default.fileExists(atPath: output.path) ? output : nil
                } : []
                let token = ImportCancellation()
                busy = true; hasError = false; activeName = batch.deviceName; cancellation = token
                destinationRoot = volume; sourceRoot = nil; protectedPhysicalIDs = [key]
                lastFolder = batch.folder
                message = "Creating Quick Share copies from saved originals."
                progress = ImportProgress(phase: "Creating sharing copies")
                let logURL = support.appendingPathComponent("imports.log")
                queue.async {
                    let validate: () throws -> Void = {
                        guard try ImportVolumes.identity(destination) == batch.destinationVolumeID,
                              try ImportVolumes.identity(batch.folder) == batch.destinationVolumeID,
                              try ImportVolumes.root(destination).standardizedFileURL == volume.standardizedFileURL,
                              ImportVolumes.physicalID(volume) == physical else {
                            throw ImportFailure("The import destination disconnected or changed. Quick Share stopped.")
                        }
                    }
                    let audit: (String) throws -> Void = { line in
                        if !FileManager.default.fileExists(atPath: logURL.path) { FileManager.default.createFile(atPath: logURL.path, contents: nil) }
                        let handle = try FileHandle(forWritingTo: logURL); defer { try? handle.close() }
                        try handle.seekToEnd()
                        try handle.write(contentsOf: Data("\(ISO8601DateFormatter().string(from: Date())) \(line)\n".utf8))
                        try handle.synchronize()
                    }
                    do {
                        try audit("User requested Quick Share | \(batch.deviceName) | \(preset.rawValue) | \(color.rawValue)")
                        let engine = EasyShareEngine(tools: tools, preset: preset, color: color,
                            camera: camera, cancellation: token,
                            validate: validate, report: { value in DispatchQueue.main.async { self.progress = value } }, audit: audit)
                        let result = try engine.run(originals: batch.originals, folder: batch.folder, stabilized: stabilized)
                        let total = result.created + result.reused
                        let note = total > 0 ? "\(batch.deviceName): \(total) \(preset.rawValue) Quick Share copies ready in Sharing Copies. Imported originals are unchanged."
                            : "\(batch.deviceName): no supported \(camera.rawValue) master videos in this completed import."
                        DispatchQueue.main.async {
                            if accessed { destination.stopAccessingSecurityScopedResource() }
                            self.finish(error: nil, message: note, refreshDevices: false)
                            if returnToEject, let entry = self.completions.entries[batch.id] { self.showEjectPrompt(entry) }
                            self.refresh()
                        }
                    } catch {
                        try? audit("Quick Share stopped; imported originals kept: \(error.localizedDescription)")
                        DispatchQueue.main.async {
                            if accessed { destination.stopAccessingSecurityScopedResource() }
                            self.finish(error: error, message: "Quick Share stopped: \(error.localizedDescription) Imported originals are unchanged.")
                        }
                    }
                }
            } catch {
                if accessed { destination.stopAccessingSecurityScopedResource() }
                throw error
            }
        } catch { recordOperationFailure(error) }
    }
    #endif

    private func ejectCameraStorage(_ entry: ImportCompletion, plan: USBStorageUnmountPlan,
                                    expectedConnections: [URL: UUID]) {
        let urls = plan.members.map(\.url)
        let keys = Set(plan.members.map(\.diskID))
        guard completions.matches(entry.id, volumeID: try? ImportVolumes.identity(entry.source),
                  physicalID: ImportVolumes.physicalID(entry.source),
                  connection: connections[entry.source] ?? UUID(), root: try? ImportVolumes.root(entry.source)),
              !busy, manualOperations.isDisjoint(with: keys) else {
            recordOperationFailure(ImportFailure("The camera is busy or the import choice expired. Try again after it finishes."))
            return
        }
        let validate: ([URL]) throws -> Void = { remaining in
            try plan.validate(remaining: remaining)
            guard remaining.allSatisfy({ self.connections[$0] == expectedConnections[$0] }) else {
                throw ImportFailure("A camera source reconnected. Ejection stopped; check all sources before unplugging.")
            }
        }
        do { try validate(urls) }
        catch { recordOperationFailure(error); return }
        for url in urls { clearCompletions(for: url) }
        manualOperations.formUnion(keys)
        busy = true; activeName = entry.deviceName; sourceRoot = entry.source
        protectedPhysicalIDs = keys; progress.phase = "Ejecting"
        logEjectEvent("User confirmed unmount of all camera storage | \(urls.map(\.lastPathComponent).joined(separator: ", "))")
        SequentialVolumeUnmount.run(volumes: urls, validate: validate) { error in
            self.manualOperations.subtract(keys)
            let result = error.map { "Camera eject stopped: \($0.localizedDescription) Some sources may already be unmounted. Check all camera storage before unplugging." }
                ?? "\(entry.deviceName): all camera storage unmounted. Safe to unplug."
            self.logEjectEvent(result)
            self.finish(error: error, message: result, notificationTitle: "Easy Eject")
        }
    }

    func handleImportNotification(id: String, action: String) {
        if action == UNNotificationDismissActionIdentifier { return }
        guard let entry = completions.entries[id] else {
            recordOperationFailure(ImportFailure("This import notification has expired. Check the connected device in the eject menu."))
            NotificationCenter.default.post(name: ImportCompletionNotification.showImports, object: nil)
            return
        }
        switch action {
        case ImportCompletionNotification.eject: showEjectPrompt(entry)
        case ImportCompletionNotification.openFolder:
            do {
                #if APP_STORE
                let access = try ScopedFolder(entry.destinationBookmark)
                defer { access.close() }
                #endif
                guard try ImportVolumes.identity(entry.folder) == entry.destinationVolumeID else {
                    throw ImportFailure("The import destination is disconnected or changed.")
                }
                NSWorkspace.shared.open(entry.folder)
            } catch {
                recordOperationFailure(error)
                NotificationCenter.default.post(name: ImportCompletionNotification.showImports, object: nil)
            }
        case UNNotificationDefaultActionIdentifier:
            NotificationCenter.default.post(name: ImportCompletionNotification.showImports, object: nil)
        default: break
        }
    }

    private func logEjectEvent(_ message: String) {
        let logURL = support.appendingPathComponent("imports.log")
        let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
        queue.async {
            do {
                let handle = try FileHandle(forWritingTo: logURL)
                defer { try? handle.close() }
                try handle.seekToEnd()
                try handle.write(contentsOf: Data(line.utf8))
                try handle.synchronize()
            } catch {
                DispatchQueue.main.async { LogManager.shared.log("Could not record eject event: \(error.localizedDescription)") }
            }
        }
    }

    func ejectCompletedImport(_ id: String) {
        guard let entry = completions.entries[id] else { return }
        // No stored payload path is trusted. All state comes from the completed import in this process.
        guard completions.matches(id, volumeID: try? ImportVolumes.identity(entry.source),
                physicalID: ImportVolumes.physicalID(entry.source), connection: connections[entry.source] ?? UUID(),
                root: try? ImportVolumes.root(entry.source)) else {
            clearCompletions(for: entry.source)
            recordOperationFailure(ImportFailure("The device disconnected or changed. Check it in the eject menu."))
            NotificationCenter.default.post(name: ImportCompletionNotification.showImports, object: nil)
            return
        }
        if !ImportVolumes.otherStorageSources(entry.source).isEmpty {
            showEjectPrompt(entry)
            return
        }
        guard !busy, reserveManualEject(entry.source) else {
            LogManager.shared.log("Eject postponed: a disk is busy importing or ejecting. Use Eject Now after it finishes.")
            NotificationCenter.default.post(name: ImportCompletionNotification.showImports, object: nil)
            return
        }
        let operationKey = entry.physicalID!
        // Consume the action once, before unmounting. Repeat clicks cannot request another eject.
        completions.remove(id); updatePendingEjects(); removeNotifications([id])
        busy = true; activeName = entry.deviceName; sourceRoot = entry.source
        protectedPhysicalIDs = [operationKey]; progress.phase = "Ejecting"
        logEjectEvent("User requested post-import eject | \(entry.deviceName) | \(entry.source.path)")
        FileManager.default.unmountVolume(at: entry.source, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
            DispatchQueue.main.async {
                self.logEjectEvent(error.map { "Post-import eject failed | \(entry.deviceName) | \($0.localizedDescription)" }
                    ?? "Post-import eject succeeded | \(entry.deviceName)")
                if error == nil { self.completedEjectID = entry.volumeID }
                self.manualOperations.remove(operationKey)
                self.finish(error: error, message: error.map { "Could not eject \(entry.deviceName): \($0.localizedDescription)" }
                    ?? "\(entry.deviceName): safe to unplug.", notificationTitle: "Easy Eject")
            }
        }
    }

    private func finish(error: Error?, message: String, notify: Bool = true, refreshDevices: Bool = true,
                        notificationTitle: String? = nil) {
        busy = false; cancellation = nil; sourceRoot = nil; destinationRoot = nil; protectedPhysicalIDs.removeAll()
        hasError = error != nil; self.message = message; progress.phase = error == nil ? "Complete" : "Needs attention"
        LogManager.shared.log(message)
        if notify && UserDefaults.standard.bool(forKey: "showEjectNotifications") {
            let content = UNMutableNotificationContent(); content.title = notificationTitle ?? (error == nil ? "Easy Eject import complete" : "Easy Eject import needs attention")
            content.body = message
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
        if refreshDevices { refresh() }
    }
}
