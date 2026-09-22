import Foundation
@main struct BulkEjectPolicyTests {
    static func main() throws {
        typealias V = BulkEjectPolicy.Volume
        func item(_ id: String, _ disk: String?, card: Bool = true, blocked: Bool = false) -> V {
            V(id: id, disk: disk, isCard: card, blocked: blocked)
        }
        func check(_ condition: Bool) { precondition(condition) }
        func denied(_ items: [V]) {
            do { _ = try BulkEjectPolicy.plan(items); fatalError("Unsafe batch accepted") } catch { }
        }
        check(try BulkEjectPolicy.plan([]).isEmpty)
        check(try BulkEjectPolicy.plan([item("A", "disk1"), item("B", "disk2")]) == [["A"], ["B"]])
        check(try BulkEjectPolicy.plan([item("A", "disk1"), item("B", "disk1")]) == [["A", "B"]])
        check(try BulkEjectPolicy.plan([item("SSD", "disk0", card: false), item("A", "disk1")]) == [["A"]])
        denied([item("A", nil)])
        denied([item("A", "disk1", blocked: true)])
        denied([item("A", "disk1"), item("Destination", "disk1", blocked: true)])
        denied([item("A", "disk1"), item("SSD", "disk1", card: false)])
        denied([item("A", "disk1"), item("B", "disk2", blocked: true)])
        print("9 bulk-eject planning cases passed")
    }
}
