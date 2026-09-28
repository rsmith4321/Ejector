import AppKit
struct ImportFailure: LocalizedError { let errorDescription: String?; init(_ message: String) { errorDescription = message } }
@main struct GroupedUnmountIntegrationTests {
    @MainActor static func main() {
        _ = NSApplication.shared
        let urls = CommandLine.arguments.dropFirst().map { URL(fileURLWithPath: $0) }
        precondition(urls.count == 2 && urls.allSatisfy { $0.lastPathComponent.hasPrefix("EE-Group-Test-") })
        let ids = Dictionary(uniqueKeysWithValues: urls.map { ($0, try! ImportVolumes.identity($0)) })
        SequentialVolumeUnmount.run(volumes: urls, validate: { remaining in
            let mounted = Set(FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: []) ?? [])
            print("expected", remaining.map(\.absoluteString), "mounted fixture", mounted.filter { $0.lastPathComponent.hasPrefix("EE-Group-Test-") }.map(\.absoluteString)); fflush(stdout)
            precondition(mounted.intersection(Set(urls)) == Set(remaining))
            for url in remaining { precondition(try! ImportVolumes.identity(url) == ids[url]) }
        }) { error in
            precondition(error == nil, error?.localizedDescription ?? "")
            precondition(Set(FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: []) ?? []).isDisjoint(with: urls))
            print("PASS actual two-volume flush/unmount without hardware eject; both absent before success")
            fflush(stdout); exit(0)
        }
        NSApp.run()
    }
}
