import Foundation

@main struct StoreEjectPolicyTests {
    static func main() {
        func allowed(_ source: String = "A", disk: String? = "disk1", profiles: Set<String> = ["A"],
                     mounted: [(String, String?)]) -> Bool {
            StoreEjectPolicy.allowsAutomaticEject(sourceID: source, sourceDisk: disk,
                                                  enrolledIDs: profiles, mounted: mounted)
        }
        // The contract permits ordinary single-profile disks, but cannot strand another import.
        precondition(allowed(mounted: [("A", "disk1")]))
        precondition(allowed(mounted: [("A", "disk1"), ("B", "disk1")]))
        precondition(!allowed(profiles: ["A", "B"], mounted: [("A", "disk1"), ("B", "disk1")]))
        precondition(!allowed("B", profiles: ["A", "B"], mounted: [("A", "disk1"), ("B", "disk1")]))
        precondition(allowed(profiles: ["A", "B"], mounted: [("A", "disk1"), ("B", "disk2")]))
        precondition(!allowed(disk: nil, mounted: [("A", nil)]))
        precondition(!allowed(mounted: []))
        precondition(!allowed(mounted: [("A", "disk2")]))
        precondition(!allowed(profiles: ["A", "B"], mounted: [("A", "disk1"), ("B", nil)]))
        print("9 automatic-eject coordination cases passed")
    }
}
