//
//  EjectorApp.swift
//  Ejector
//
//  Created by Ryan Smith on 5/6/26.
//

import SwiftUI
import Cocoa
import Combine

// MARK: - 1. Drive Model
struct Drive: Identifiable {
    let id = UUID()
    let name: String
    let url: URL
    let isCameraOrRemovableMedia: Bool
}

// MARK: - 2. Drive Manager (The Brains)
class DriveManager: ObservableObject {
    @Published var drives: [Drive] = []
    
    // Fetches currently mounted removable volumes
    func fetchDrives() {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsRemovableKey, .volumeIsEjectableKey, .volumeIsInternalKey]
        
        guard let paths = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) else {
            return
        }
        
        var foundDrives: [Drive] = []
        
        for url in paths {
            guard let components = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            
            // 1. Skip the Mac's main internal hard drive entirely
            if components.volumeIsInternal == true {
                continue
            }
            
            // 2. Process external drives
            if components.volumeIsRemovable == true || components.volumeIsEjectable == true {
                let name = components.volumeName ?? url.lastPathComponent
                
                // Check A: Does macOS think it's an SD card or USB stick?
                var isCameraMedia = components.volumeIsRemovable == true
                
                // Check B: The DCIM Heuristic (Catches CFExpress cards)
                if !isCameraMedia {
                    let dcimURL = url.appendingPathComponent("DCIM")
                    var isDirectory: ObjCBool = false
                    if FileManager.default.fileExists(atPath: dcimURL.path, isDirectory: &isDirectory), isDirectory.boolValue {
                        isCameraMedia = true
                    }
                }
                
                // (Optional Check C): Check the name as a fallback
                if name.uppercased().contains("CFEXPRESS") {
                    isCameraMedia = true
                }
                
                foundDrives.append(Drive(name: name, url: url, isCameraOrRemovableMedia: isCameraMedia))
            }
        }
        
        DispatchQueue.main.async {
            self.drives = foundDrives
        }
    }
    
    // Ejects the selected drive
    func eject(drive: Drive) {
        FileManager.default.unmountVolume(at: drive.url, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
            DispatchQueue.main.async {
                if let error = error {
                    print("Failed to eject \(drive.name): \(error.localizedDescription)")
                } else {
                    print("Successfully ejected \(drive.name)")
                    self.fetchDrives() // Refresh the list
                }
            }
        }
    }
}

// MARK: - 3. The Menu Bar View
struct EjectorMenuView: View {
    @StateObject private var manager = DriveManager()
    
    // Updated to use our smart property
    var removableMedia: [Drive] { manager.drives.filter { $0.isCameraOrRemovableMedia } }
    var externalDisks: [Drive] { manager.drives.filter { !$0.isCameraOrRemovableMedia } }
    
    var body: some View {
        VStack(alignment: .leading) {
            if manager.drives.isEmpty {
                Text("No external drives found")
                    .foregroundColor(.secondary)
            } else {
                
                // Section 1: Camera Cards (SD, CFExpress) and USBs
                if !removableMedia.isEmpty {
                    Text("Camera Cards & USBs")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .padding(.horizontal)
                        .padding(.top, 4)
                    
                    ForEach(removableMedia) { drive in
                        Button(action: { manager.eject(drive: drive) }) {
                            Text("⏏️ Eject \(drive.name)")
                        }
                    }
                }
                
                if !removableMedia.isEmpty && !externalDisks.isEmpty {
                    Divider()
                }
                
                // Section 2: Pure External SSDs and Hard Drives
                if !externalDisks.isEmpty {
                    Text("External SSDs")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .padding(.horizontal)
                        .padding(.top, 4)
                    
                    ForEach(externalDisks) { drive in
                        Button(action: { manager.eject(drive: drive) }) {
                            Text("⏏️ Eject \(drive.name)")
                        }
                    }
                }
            }
            
            Divider()
            
            Button("Refresh List") {
                manager.fetchDrives()
            }
            
            Button("Quit Ejector") {
                NSApplication.shared.terminate(nil)
            }
        }
        .onAppear {
            manager.fetchDrives()
        }
    }
}

// MARK: - 4. The App Entry Point
@main
struct EjectorApp: App {
    var body: some Scene {
        // MenuBarExtra tells macOS to put this in the top right menu bar, not in a window
        MenuBarExtra("Ejector", systemImage: "eject.fill") {
            EjectorMenuView()
        }
    }
}
