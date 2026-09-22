import SwiftUI

/// The same permission instructions appear in first-run help and Settings.
struct FolderAuthorizationInstructions: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Eject works without folder access.").fontWeight(.semibold)
            Text("To clean cards or detect camera folders:")
            Text("1. Click Authorize a Card.")
            Text("2. Under Locations, select the whole card (for example, Untitled), not DCIM. Click Authorize.")
            Text("3. Turn on Clean Cards Before Ejecting for automatic metadata cleanup. Your photos and videos are kept.")
            Text("Access is remembered. Authorize again after formatting; use Forget to remove access.")
                .foregroundStyle(.secondary)
            DisclosureGroup("Optional imports & troubleshooting") {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Import setup asks for the recording folder (such as DCIM) and a destination on another disk. Device Trash recovery also needs whole-card access.")
                    Text("Authorizing a folder never starts imports or enables deletion. You don't need Full Disk Access or Accessibility.")
                    Text("If access is denied, reconnect the card and authorize the card or import folders again.")
                }.padding(.top, 6)
            }
        }
        .font(.system(size: 14))
        .fixedSize(horizontal: false, vertical: true)
    }
}
