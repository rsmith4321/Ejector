import Foundation

struct ImportFailure: LocalizedError { let errorDescription: String?; init(_ message: String) { errorDescription = message } }

@main struct GroupedUnmountTests {
    @MainActor static func main() {
        let a = URL(fileURLWithPath: "/test/Internal")
        let b = URL(fileURLWithPath: "/test/SD Card")
        var remaining = [a, b]
        var calls: [URL] = []
        var pending: (@MainActor (Error?) -> Void)?
        var finished = false
        SequentialVolumeUnmount.run(volumes: [a, b], validate: { expected in
            precondition(expected == remaining)
        }, unmount: { url, done in calls.append(url); pending = done }, completion: { error in
            precondition(error == nil); finished = true
        })
        precondition(calls == [a] && !finished)
        remaining.removeFirst(); pending?(nil)
        precondition(calls == [a, b] && !finished)
        remaining.removeFirst(); pending?(nil)
        precondition(finished)
        print("PASS serial unmount, success only after every source is absent")

        calls = []; finished = false
        SequentialVolumeUnmount.run(volumes: [a,b], validate: { _ in }, unmount: { url, done in
            calls.append(url); done(ImportFailure("Disk busy"))
        }, completion: { error in precondition(error != nil); finished = true })
        precondition(calls == [a] && finished)
        print("PASS failure halts before touching the other source")

        calls = []; finished = false
        SequentialVolumeUnmount.run(volumes: [a,b], validate: { expected in
            if expected == [b] { throw ImportFailure("Reconnected or extra source") }
        }, unmount: { url, done in calls.append(url); done(nil) }, completion: { error in
            precondition(error != nil); finished = true
        })
        precondition(calls == [a] && finished)
        print("PASS topology change halts between sources")

        finished = false
        SequentialVolumeUnmount.run(volumes: [a,b], validate: { expected in
            if expected.isEmpty { throw ImportFailure("New source appeared") }
        }, unmount: { _, done in done(nil) }, completion: { error in
            precondition(error != nil); finished = true
        })
        precondition(finished)
        print("PASS final verification failure never reports safe to unplug")
    }
}
