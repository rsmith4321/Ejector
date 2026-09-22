#!/bin/zsh
set -eu
cd "${0:A:h}/../.."
output="${1:?Pass an output directory}"
mkdir -p "$output/Sandbox Engine Tests.app/Contents/MacOS"
app="$output/Sandbox Engine Tests.app"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>com.ryansmithphotography.EasyEject.sandboxtests</string><key>CFBundleExecutable</key><string>SandboxEngineTests</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>
PLIST
xcrun swiftc -D APP_STORE Ejector/MediaImportEngine.swift Ejector/ScopedFolder.swift Ejector/MetadataCleaner.swift Tests/ImportEngineTests.swift -o "$app/Contents/MacOS/SandboxEngineTests"
codesign --force --sign 'Developer ID Application: Ryan Smith Photography, LLC (MCJMHBLT27)' --options runtime --entitlements Store/Store.entitlements "$app"
printf 'Disposable sandbox denial sentinel\n' > "$output/not-authorized.txt"
cat "$output/not-authorized.txt" > /dev/null
"$app/Contents/MacOS/SandboxEngineTests" "$output/not-authorized.txt"
rm "$output/not-authorized.txt"
