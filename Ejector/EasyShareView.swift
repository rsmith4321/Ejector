#if !APP_STORE
import SwiftUI
import UniformTypeIdentifiers

struct EasyShareProfileOptions: View {
    @Binding var profile: DroneProfile
    @State private var error: String?
    @State private var lutReady = (try? EasyShareTools.validateLUT(EasyShareTools.folder.appendingPathComponent(EasyShareTools.lutName))) != nil
    var body: some View {
        GroupBox("Easy Share · Insta360 Luna") {
            VStack(alignment: .leading, spacing: 10) {
                Toggle("Create sharing copies on import", isOn: Binding(
                    get: { profile.sharingPreset != nil },
                    set: { profile.sharingPreset = $0 ? .hd : nil }
                ))
                Text("Saves compressed MP4 videos in Sharing Copies inside the dated import folder. Your imported originals stay full quality.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if profile.sharingPreset != nil {
                    Picker("Sharing size", selection: Binding(get: { profile.sharingPreset ?? .hd }, set: { profile.sharingPreset = $0 })) {
                        Text("1080p · smaller files").tag(EasySharePreset.hd)
                        Text("4K · more detail").tag(EasySharePreset.uhd)
                    }
                    Picker("Camera color", selection: Binding(get: { profile.sharingColor ?? .lunaILog }, set: { profile.sharingColor = $0 })) {
                        ForEach(EasyShareColor.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                    }
                    Text("Choose the mode you used when recording. This choice applies to every Luna video in this import.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    Text("Keeps audio, landscape or portrait framing, and up to 30 fps. Smaller clips keep their size. Targets about 60 MB per minute at 1080p, or 180 MB at 4K.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    if (profile.sharingColor ?? .lunaILog) == .lunaILog {
                        Text(lutReady ? "Official Luna Rec.709 LUT ready." : "Choose the official Luna Rec.709 s33 v2 LUT once for this Mac.")
                            .font(.caption).foregroundStyle(lutReady ? Color.secondary : Color.orange)
                        Button("Choose Luna LUT…", action: chooseLUT)
                    }
                    if (try? EasyShareTools.installed()) == nil {
                        Text("Requires FFmpeg on this Mac. This is currently an option in the Website edition.")
                            .font(.caption).foregroundStyle(.orange)
                    }
                    if let error { Text(error).font(.caption).foregroundStyle(.orange) }
                }
            }.padding(6)
        }
    }
    private func chooseLUT() {
        let panel = NSOpenPanel(); panel.canChooseDirectories = false; panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [UTType(filenameExtension: "cube") ?? .data]
        panel.message = "Choose Luna_I-Log_to_Rec709_BT1886_s33_v2.cube from Insta360’s Luna LUT download."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try EasyShareTools.installLUT(from: url); lutReady = true; error = nil }
        catch { self.error = error.localizedDescription }
    }
}
#endif
