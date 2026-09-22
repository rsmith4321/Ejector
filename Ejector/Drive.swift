import Foundation

// MARK: - Card Type Classification
nonisolated enum CardType: String {
    case sd = "SD"
    case cfexpress = "CFexpress"
    case xqd = "XQD"
    case unknown = "Unknown"
}

// MARK: - 1. Drive Model
nonisolated struct Drive: Identifiable {
    var id: String { url.path }
    let name: String
    let url: URL
    let isCameraCard: Bool
    let isEmulatorCard: Bool
    let cardType: CardType?
    let isEjectable: Bool
    let isRemovable: Bool
    let isInternal: Bool

    var displayName: String {
        if let cardType = cardType, cardType != .unknown {
            return "\(name) (\(cardType.rawValue))"
        }
        return name
    }

    var iconName: String {
        if isCameraCard {
            return "sdcard"
        } else if isEmulatorCard {
            return "gamecontroller"
        } else {
            return "externaldrive"
        }
    }
}

