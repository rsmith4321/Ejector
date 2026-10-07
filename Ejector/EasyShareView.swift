#if !APP_STORE
import SwiftUI
import UniformTypeIdentifiers

struct EasyShareProfileOptions: View {
    @Binding var profile: DroneProfile
    @State private var error: String?
    @State private var lutReady = false
    private var camera: EasyShareCamera { profile.sharingCamera ?? .luna }
    private var lutName: String { camera == .luna ? EasyShareTools.lutName : EasyShareTools.djiLUTName }
    private var color: EasyShareColor { camera.logColor }
    var body: some View {
        GroupBox("Instant LUT · Quick Share") {
            VStack(alignment: .leading, spacing: 10) {
                Toggle("Offer Quick Share for this camera", isOn: Binding(
                    get: { profile.sharingPreset != nil },
                    set: { profile.sharingPreset = $0 ? .hd : nil }
                ))
                Text("Adds Quick Share to the completed-import eject dialog and Device Media Imports. Copies are made only when you choose Quick Share. Your imported originals stay full quality.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if profile.sharingPreset != nil {
                    Picker("Camera", selection: Binding(get: { camera }, set: { selected in
                        profile.sharingCamera = selected
                        profile.sharingColor = selected.logColor
                        refreshLUT()
                    })) {
                        Text("Insta360 Luna").tag(EasyShareCamera.luna)
                        Text("DJI O4 Pro").tag(EasyShareCamera.djiO4Pro)
                    }
                    Picker("Preferred sharing size", selection: Binding(get: { profile.sharingPreset ?? .hd }, set: { profile.sharingPreset = $0 })) {
                        Text("1080p · smaller files").tag(EasySharePreset.hd)
                        Text("4K · more detail").tag(EasySharePreset.uhd)
                    }
                    Text("Each Quick Share asks for 1080p or 4K and whether to apply the \(camera == .luna ? "Luna I-Log" : "DJI O4 D-Log M") LUT. Leave the LUT off for normal-color footage.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    if camera == .djiO4Pro {
                        Text("Stabilize the imported originals in Gyroflow first. Export beside them. You can apply a LUT in Gyroflow or with Quick Share; recognized Gyroflow LUT exports keep their existing colors.")
                            .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    }
                    Text("Keeps audio, landscape or portrait framing, and up to 30 fps. Smaller clips keep their size. Targets about 60 MB per minute at 1080p, or 180 MB at 4K.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    Text(lutReady ? "Official \(camera.rawValue) Rec.709 LUT ready." : "Choose the official \(camera.rawValue) Rec.709 LUT once for this Mac.")
                        .font(.caption).foregroundStyle(lutReady ? Color.secondary : Color.orange)
                    Button("Choose \(camera.rawValue) LUT…", action: chooseLUT)
                    if (try? EasyShareTools.installed()) == nil {
                        Text("Requires FFmpeg on this Mac. This is currently an option in the Website edition.")
                            .font(.caption).foregroundStyle(.orange)
                    }
                    if let error { Text(error).font(.caption).foregroundStyle(.orange) }
                }
            }.padding(6)
        }
        .onAppear(perform: refreshLUT)
    }
    private func refreshLUT() {
        lutReady = (try? EasyShareTools.validateLUT(EasyShareTools.folder.appendingPathComponent(lutName), color: color)) != nil
    }
    private func chooseLUT() {
        let panel = NSOpenPanel(); panel.canChooseDirectories = false; panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [UTType(filenameExtension: "cube") ?? .data]
        panel.message = camera == .luna
            ? "Choose Luna_I-Log_to_Rec709_BT1886_s33_v2.cube from Insta360’s Luna LUT download."
            : "Choose the O4 Air Unit Series D-Log M to Rec.709 .cube from DJI’s O4 download page."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try EasyShareTools.installLUT(from: url, color: color); refreshLUT(); error = nil }
        catch { self.error = error.localizedDescription }
    }
}

struct QuickShareCompletedImports: View {
    @ObservedObject var importer: DroneImportManager
    var body: some View {
        if !importer.quickShareImports.isEmpty {
            GroupBox("Completed imports · Quick Share") {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Make sharing copies after import, even after ejecting the device. For DJI O4 Pro, open the folder, batch stabilize in Gyroflow, then use Quick Share for smaller sharing copies. Recognized Gyroflow LUT exports keep their existing colors. The destination drive must be connected.")
                        .font(.caption).foregroundStyle(.secondary)
                    ForEach(importer.quickShareImports) { batch in
                        HStack(alignment: .top) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(batch.deviceName).font(.headline)
                                Text(batch.date, format: .dateTime.month().day().hour().minute()).font(.caption)
                                Text(batch.folder.path).font(.caption).foregroundStyle(.secondary)
                                    .lineLimit(2).textSelection(.enabled)
                            }
                            Spacer()
                            if importer.quickShareProfile(for: batch.id)?.sharingCamera == .djiO4Pro {
                                Button("Open Folder") { importer.openImportedFolder(batch.id) }
                                    .disabled(importer.busy)
                            }
                            Button("Quick Share…") { importer.quickShareCompletedImport(batch.id) }
                                .disabled(importer.busy)
                        }
                    }
                }.frame(maxWidth: .infinity, alignment: .leading).padding(6)
            }
        }
    }
}
#endif
