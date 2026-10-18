#if !APP_STORE
import SwiftUI

/// The AirDrop Inbox section of Settings (Website edition only).
struct AirDropInboxView: View {
    @ObservedObject private var inbox = AirDropInboxModel.shared
    @State private var showActivity = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("AirDrop Inbox").font(.headline)
            Toggle("Sort AirDrops automatically", isOn: $inbox.enabled)
            Text("Moves files AirDropped to this Mac out of Downloads and turns HEIC photos into JPEGs. Browser downloads and other files are left alone.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if let from = inbox.migratedFrom, inbox.enabled, inbox.activity.count <= 1 {
                Text("Settings were imported from Automatic HEIC to JPEG (\(from)).")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Group {
                Toggle("Move AirDrops to a folder", isOn: $inbox.useDestination)
                HStack {
                    Text((inbox.destinationPath as NSString).abbreviatingWithTildeInPath)
                        .lineLimit(1).truncationMode(.middle)
                        .foregroundStyle(inbox.useDestination ? .primary : .secondary)
                    Spacer()
                    Button("Choose…") { inbox.chooseDestination() }
                }
                Toggle("Convert HEIC photos to JPEG", isOn: $inbox.convert)
                Picker("After converting", selection: $inbox.originals) {
                    ForEach(AirDropInbox.OriginalsMode.allCases) { Text($0.label).tag($0) }
                }.disabled(!inbox.convert)
                HStack {
                    Text("JPEG quality")
                    Slider(value: $inbox.quality, in: 0.5...1, step: 0.05)
                    Text("\(Int((inbox.quality * 100).rounded()))%").monospacedDigit().frame(width: 44, alignment: .trailing)
                }.disabled(!inbox.convert)
                Toggle("Also convert other HEIC files in Downloads", isOn: $inbox.convertOtherHEIC).disabled(!inbox.convert)
                Text("Converts HEIC files that reach Downloads some other way, in place. AirDropped files are always handled.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }.disabled(!inbox.enabled)
            if inbox.enabled {
                statusRow
                if inbox.earlier > 0 {
                    HStack {
                        Text("\(inbox.earlier) earlier AirDrop\(inbox.earlier == 1 ? "" : "s") in Downloads")
                            .font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Button("Process Earlier AirDrops") { inbox.processEarlier() }
                    }
                }
                HStack {
                    Button("Open AirDrop Folder") { inbox.openOutputFolder() }
                    Button(showActivity ? "Hide Recent Activity" : "Show Recent Activity") { showActivity.toggle() }
                        .disabled(inbox.activity.isEmpty)
                }
                if showActivity {
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(inbox.activity) { entry in
                            VStack(alignment: .leading, spacing: 1) {
                                Text(entry.title).font(.caption).lineLimit(1).truncationMode(.middle)
                                Text("\(entry.date.formatted(date: .abbreviated, time: .shortened)) · \(entry.detail)")
                                    .font(.caption2).foregroundStyle(entry.kind == .failed || entry.kind == .warning ? .orange : .secondary)
                            }
                        }
                        HStack {
                            Button("Open Log") { inbox.openLog() }
                            Button("Clear") { inbox.clearActivity(); showActivity = false }
                        }.controlSize(.small)
                    }
                }
            }
        }
    }

    @ViewBuilder private var statusRow: some View {
        switch inbox.status {
        case .watching, .off:
            Label(inbox.statusText, systemImage: "checkmark.circle").font(.caption).foregroundStyle(.secondary)
        case .needsAccess:
            VStack(alignment: .leading, spacing: 6) {
                Label(inbox.statusText, systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Open Privacy Settings") { inbox.openPrivacySettings() }
                    Button("Try Again") { inbox.restart() }
                }
            }
        case .destinationMissing, .standaloneRunning:
            Label(inbox.statusText, systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
#endif
