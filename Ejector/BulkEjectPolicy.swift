import Foundation

/// An eject unmounts the whole physical disk. Plan before starting any work.
nonisolated enum BulkEjectPolicy {
    struct Volume {
        let id: String
        let disk: String?
        let isCard: Bool
        let blocked: Bool
    }
    static func plan(_ volumes: [Volume]) throws -> [[String]] {
        let cards = volumes.filter(\.isCard)
        guard cards.allSatisfy({ $0.disk != nil }) else {
            throw ImportFailure("A card's physical disk could not be identified. Eject it individually after checking its partitions.")
        }
        var result: [[String]] = []
        var seen = Set<String>()
        for card in cards {
            guard let disk = card.disk, seen.insert(disk).inserted else { continue }
            let siblings = volumes.filter { $0.disk == disk }
            guard !siblings.contains(where: \.blocked) else {
                throw ImportFailure("A card shares a disk with an active operation. Wait for it to finish before ejecting all cards.")
            }
            guard siblings.allSatisfy(\.isCard) else {
                throw ImportFailure("A card shares its disk with another volume. Review the partitions and eject that disk individually.")
            }
            result.append(siblings.map(\.id))
        }
        return result
    }
}
