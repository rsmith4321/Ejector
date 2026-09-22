import SwiftUI

/// The same permission instructions appear in first-run help and Settings.
struct FolderAuthorizationInstructions: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Plain Eject needs no folder authorization. You can keep importing photos with Lightroom or your usual app.")
            Text("For Clean & Eject and camera/emulator folder detection:").fontWeight(.semibold)
            Text("1. Choose Authorize a Card from the eject menu or Settings.")
            Text("2. In the folder chooser, select the card's name under Locations. Select the card itself, such as Untitled, not DCIM or a folder inside it.")
            Text("3. Click Authorize. Then enable Clean Cards Before Ejecting in Settings if you want cleanup with your card's eject button.")
            Text("Access is remembered for that volume. After formatting the card, authorize it again. Forget in Settings removes the saved authorization.")
                .foregroundStyle(.secondary)
            Text("Optional imports have separate permissions: select the media folder, such as DCIM, and a destination folder on a different disk. Device Trash recovery also needs authorization for the whole card. Granting access never starts an import or turns on deletion.")
                .foregroundStyle(.secondary)
            Text("You do not need Full Disk Access or Accessibility. If access is denied, reconnect the card and authorize the same card or import folders again.")
                .foregroundStyle(.secondary)
        }.fixedSize(horizontal: false, vertical: true)
    }
}
