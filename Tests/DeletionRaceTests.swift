import Foundation
import Darwin

// Concurrent-replacement regressions for the overnight review's deletion races
// (release-evidence/overnight-review-2026-09-24). Each case swaps a checked path
// at a scheduling hook, mirroring the review's seam probes, and uses only
// generated fixtures in a temporary folder. Compile with -D EASY_EJECT_TEST_SEAMS.
@main struct DeletionRaceTests {
    static func main() throws {
        var passed = 0, failed = 0
        let fm = FileManager.default
        // checkPath rejects the /var link, and standardizedFileURL maps /private/var
        // back to /var, so the runner passes a link-free temporary fixture folder.
        guard CommandLine.arguments.count == 2 else { throw ImportFailure("Pass a link-free temporary fixture folder.") }
        let temporary = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
        func test(_ name: String, _ body: (URL, URL, URL, URL) throws -> Void) throws {
            let base = temporary.appendingPathComponent("easy-eject-race-test-\(UUID().uuidString)")
            let card = base.appendingPathComponent("card"), media = card.appendingPathComponent("DCIM"), dest = base.appendingPathComponent("destination")
            try fm.createDirectory(at: media, withIntermediateDirectories: true)
            try fm.createDirectory(at: dest, withIntermediateDirectories: true)
            defer {
                DeletionRaceHooks.afterFinalStamp = nil; DeletionRaceHooks.afterParentPinned = nil; DeletionRaceHooks.beforeMetadataUnlink = nil
                try? fm.removeItem(at: base)
            }
            // Report every case so a regression shows each race it reopens.
            do { try body(base, card, media, dest); passed += 1; print("PASS \(name)") }
            catch { failed += 1; print("FAIL \(name): \(error.localizedDescription)") }
        }
        func check(_ condition: Bool, _ message: String) throws { if !condition { throw ImportFailure("TEST FAILED: \(message)") } }
        func exists(_ url: URL) -> Bool { var s = stat(); return lstat(url.path, &s) == 0 }
        func engine(_ card: URL, _ media: URL, _ dest: URL, delete: Bool = true, trash: Bool = false) -> MediaImportEngine {
            MediaImportEngine(source: card, mediaFolder: media, destination: dest, deleteOriginals: delete, recoverTrash: trash,
                              cancellation: ImportCancellation(), validate: {}, report: { _ in }, audit: { _ in })
        }
        func failure(_ body: () throws -> Void) -> String? {
            do { try body(); return nil } catch { return error.localizedDescription }
        }
        let verified = Data("VERIFIED ORIGINAL CONTENT".utf8), unverified = Data("UNVERIFIED NEW MEDIA".utf8)
        func copied(_ dest: URL, _ relative: String) -> Bool {
            let days = (try? fm.contentsOfDirectory(atPath: dest.path)) ?? []
            return days.contains { (try? Data(contentsOf: dest.appendingPathComponent($0).appendingPathComponent(relative))) == verified }
        }

        try test("original replaced by a directory after the final check is not removed") { base, card, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try verified.write(to: file)
            DeletionRaceHooks.afterFinalStamp = {
                DeletionRaceHooks.afterFinalStamp = nil
                try! fm.moveItem(at: file, to: base.appendingPathComponent("parked-verified-original"))
                try! fm.createDirectory(at: file, withIntermediateDirectories: false)
                try! unverified.write(to: file.appendingPathComponent("unverified-recording.mp4"))
            }
            let error = failure { _ = try engine(card, media, dest).run() }
            try check(error?.contains("changed before deletion") == true, "deletion refused and reported: \(error ?? "success")")
            try check((try? Data(contentsOf: file.appendingPathComponent("unverified-recording.mp4"))) == unverified, "unverified media survived")
            try check(exists(base.appendingPathComponent("parked-verified-original")), "parked original untouched")
            try check(copied(dest, "clip.mp4"), "verified copy kept")
        }
        try test("original replaced by another regular file is not removed") { base, card, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try verified.write(to: file)
            DeletionRaceHooks.afterFinalStamp = {
                DeletionRaceHooks.afterFinalStamp = nil
                try! fm.moveItem(at: file, to: base.appendingPathComponent("parked-verified-original"))
                try! unverified.write(to: file)
            }
            let error = failure { _ = try engine(card, media, dest).run() }
            try check(error?.contains("changed before deletion") == true, "deletion refused and reported: \(error ?? "success")")
            try check((try? Data(contentsOf: file)) == unverified, "replacement file survived")
        }
        try test("recovered Trash file replaced by a directory is not removed") { base, card, media, dest in
            let trash = card.appendingPathComponent(".Trashes/\(getuid())"); try fm.createDirectory(at: trash, withIntermediateDirectories: true)
            let file = trash.appendingPathComponent("deleted.mp4"); try verified.write(to: file)
            DeletionRaceHooks.afterFinalStamp = {
                DeletionRaceHooks.afterFinalStamp = nil
                try! fm.moveItem(at: file, to: base.appendingPathComponent("parked-trash-original"))
                try! fm.createDirectory(at: file, withIntermediateDirectories: false)
                try! unverified.write(to: file.appendingPathComponent("unverified-recording.mp4"))
            }
            let error = failure { _ = try engine(card, media, dest, delete: false, trash: true).run() }
            try check(error?.contains("changed before deletion") == true, "deletion refused and reported: \(error ?? "success")")
            try check((try? Data(contentsOf: file.appendingPathComponent("unverified-recording.mp4"))) == unverified, "unverified Trash media survived")
            try check(exists(base.appendingPathComponent("parked-trash-original")), "parked Trash original untouched")
            try check(copied(dest, "Recovered Device Trash/deleted.mp4"), "verified Trash copy kept")
        }
        try test("ancestor swapped for a link before deletion does not reach outside the card") { base, card, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try verified.write(to: file)
            let outside = base.appendingPathComponent("outside"); try fm.createDirectory(at: outside, withIntermediateDirectories: true)
            let victim = outside.appendingPathComponent("clip.mp4"); try verified.write(to: victim)
            DeletionRaceHooks.afterFinalStamp = {
                DeletionRaceHooks.afterFinalStamp = nil
                try! fm.moveItem(at: media, to: base.appendingPathComponent("parked-DCIM"))
                try! fm.createSymbolicLink(at: media, withDestinationURL: outside)
            }
            let error = failure { _ = try engine(card, media, dest).run() }
            try check(error?.contains("changed before deletion") == true, "deletion refused and reported: \(error ?? "success")")
            try check((try? Data(contentsOf: victim)) == verified, "outside file survived")
            try check(exists(base.appendingPathComponent("parked-DCIM/clip.mp4")), "parked original untouched")
        }
        try test("ancestor swapped after the parent is pinned stays on the verified folder") { base, card, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try verified.write(to: file)
            let outside = base.appendingPathComponent("outside"); try fm.createDirectory(at: outside, withIntermediateDirectories: true)
            let victim = outside.appendingPathComponent("clip.mp4"); try verified.write(to: victim)
            DeletionRaceHooks.afterParentPinned = {
                DeletionRaceHooks.afterParentPinned = nil
                try! fm.moveItem(at: media, to: base.appendingPathComponent("parked-DCIM"))
                try! fm.createSymbolicLink(at: media, withDestinationURL: outside)
            }
            // Only the verified file in the pinned folder is removed; the later
            // manifest check then refuses the changed card before ejecting.
            let error = failure { _ = try engine(card, media, dest).run() }
            try check(error != nil, "changed card reported")
            try check((try? Data(contentsOf: victim)) == verified, "outside file survived")
            try check(!exists(base.appendingPathComponent("parked-DCIM/clip.mp4")), "only the verified original was removed")
            try check(copied(dest, "clip.mp4"), "verified copy kept")
        }
        try test("cleanup ancestor swapped for a link does not reach outside the card") { base, card, _, _ in
            let sub = card.appendingPathComponent("sub"), outside = base.appendingPathComponent("outside")
            try fm.createDirectory(at: sub, withIntermediateDirectories: true); try fm.createDirectory(at: outside, withIntermediateDirectories: true)
            try Data("inside metadata fixture".utf8).write(to: sub.appendingPathComponent("._victim"))
            let victim = outside.appendingPathComponent("._victim"); try Data("OUTSIDE AUTHORIZED CLEANUP ROOT".utf8).write(to: victim)
            DeletionRaceHooks.beforeMetadataUnlink = {
                DeletionRaceHooks.beforeMetadataUnlink = nil
                try! fm.moveItem(at: sub, to: base.appendingPathComponent("parked-sub"))
                try! fm.createSymbolicLink(at: sub, withDestinationURL: outside)
            }
            let count = try MetadataCleaner.clean(card, validate: {})
            try check(count == 1, "one card metadata file cleaned")
            try check(exists(victim), "outside fixture survived")
            try check(!exists(base.appendingPathComponent("parked-sub/._victim")), "only the card's own metadata was removed")
        }
        try test("cleanup metadata replaced by a directory is refused") { base, card, _, _ in
            let metadata = card.appendingPathComponent("._clip.mp4"); try Data("metadata".utf8).write(to: metadata)
            DeletionRaceHooks.beforeMetadataUnlink = {
                DeletionRaceHooks.beforeMetadataUnlink = nil
                try! fm.moveItem(at: metadata, to: base.appendingPathComponent("parked-metadata"))
                try! fm.createDirectory(at: metadata, withIntermediateDirectories: false)
                try! unverified.write(to: metadata.appendingPathComponent("unverified-recording.mp4"))
            }
            let error = failure { _ = try MetadataCleaner.clean(card, validate: {}) }
            try check(error?.contains("changed during cleanup") == true, "cleanup refused and reported: \(error ?? "success")")
            try check((try? Data(contentsOf: metadata.appendingPathComponent("unverified-recording.mp4"))) == unverified, "substituted directory contents survived")
        }
        try test("cleanup root swapped for a link is refused") { base, card, _, _ in
            let outside = base.appendingPathComponent("outside"); try fm.createDirectory(at: outside, withIntermediateDirectories: true)
            let victim = outside.appendingPathComponent(".DS_Store"); try Data("outside".utf8).write(to: victim)
            try fm.moveItem(at: card, to: base.appendingPathComponent("parked-card"))
            try fm.createSymbolicLink(at: card, withDestinationURL: outside)
            try check(failure { _ = try MetadataCleaner.clean(card, validate: {}) } != nil, "linked root refused")
            try check(exists(victim), "outside fixture survived")
        }
        try test("cleanup skips protected system folders and unreadable folders") { _, card, _, _ in
            let system = card.appendingPathComponent(".DocumentRevisions-V100"), locked = card.appendingPathComponent("locked")
            let open = card.appendingPathComponent("open")
            for folder in [system, locked, open] { try fm.createDirectory(at: folder, withIntermediateDirectories: true) }
            for folder in [system, locked, open] { try Data("metadata".utf8).write(to: folder.appendingPathComponent("._clip.mp4")) }
            // Mode 0111 matches what macOS sets on .DocumentRevisions-V100: searchable, not readable.
            chmod(system.path, 0o111); chmod(locked.path, 0o111)
            defer { chmod(system.path, 0o755); chmod(locked.path, 0o755) }
            let count = try MetadataCleaner.clean(card, validate: {})
            try check(count == 1, "only the readable folder was cleaned: \(count)")
            chmod(system.path, 0o755); chmod(locked.path, 0o755)
            try check(exists(system.appendingPathComponent("._clip.mp4")), "system folder left alone")
            try check(exists(locked.appendingPathComponent("._clip.mp4")), "unreadable folder left alone")
            try check(!exists(open.appendingPathComponent("._clip.mp4")), "readable folder cleaned")
        }
        print("\(passed) tests passed, \(failed) failed")
        if failed > 0 { exit(1) }
    }
}
