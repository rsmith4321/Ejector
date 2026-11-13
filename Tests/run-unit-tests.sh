#!/bin/zsh
# Compiles and runs the self-contained unit test executables against this worktree.
# Not run here, because they need things a unit run does not have:
#   ImportEjectPromptTests (shows real dialogs on screen), EasyShareTests, DJIO4QuickShareTests and
#   GyroflowLUTBridgeTests (sample footage, a LUT and ffmpeg), MountedVolumeImportTest (a test disk),
#   and the *IntegrationTests, which the run-*-integration.py scripts drive with a signed app.
set -u
cd "${0:A:h}/.."
out=build/tests; mkdir -p $out; rc=0
run() { local name=$1; shift; local flags=(); [[ $1 == -D* ]] && { flags=($1 $2); shift 2 }
  echo "== $name"
  if ! xcrun swiftc $flags "$@" -o $out/$name > $out/$name.compile.log 2>&1; then echo "COMPILE FAILED"; tail -20 $out/$name.compile.log; rc=1; return; fi
  $out/$name $RUNARG 2>&1 | tee $out/$name.log; [[ $pipestatus[1] == 0 ]] || { echo "EXIT $pipestatus[1]"; rc=1 }
}
fixtures=$(mktemp -d "$PWD/build/race-fixtures.XXXXXX")
RUNARG=$fixtures run DeletionRaceTests -D EASY_EJECT_TEST_SEAMS Ejector/MediaImportEngine.swift Ejector/MetadataCleaner.swift Tests/DeletionRaceTests.swift
sentinel=$(mktemp -d "$PWD/build/sentinel.XXXXXX")/not-used.txt
RUNARG=$sentinel run ImportEngineTests Ejector/MediaImportEngine.swift Ejector/ScopedFolder.swift Ejector/MetadataCleaner.swift Tests/ImportEngineTests.swift
RUNARG="" 
run BulkEjectPolicyTests Ejector/MediaImportEngine.swift Ejector/BulkEjectPolicy.swift Tests/BulkEjectPolicyTests.swift
run AirDropInboxTests Ejector/AirDropInboxCore.swift Tests/AirDropInboxTests.swift
run StoreEjectPolicyTests -D APP_STORE Ejector/StoreEjectPolicy.swift Tests/StoreEjectPolicyTests.swift
run GroupedUnmountTests Ejector/ImportVolumes.swift Tests/GroupedUnmountTests.swift
run ImportCompletionNotificationTests Ejector/ImportCompletionNotification.swift Tests/ImportCompletionNotificationTests.swift
run ImportDefaultsTests Ejector/ImportDefaults.swift Ejector/DroneProfile.swift Ejector/EasyShare.swift Ejector/MediaImportEngine.swift Tests/ImportDefaultsTests.swift
products=build/DerivedData-Website/SourcePackages/artifacts/sparkle/Sparkle/Sparkle.xcframework/macos-arm64_x86_64
run UpdateSafetyTests -F $products -Xlinker -rpath -Xlinker $PWD/$products -framework Sparkle Ejector/UpdateChannel.swift Tests/UpdateSafetyTests.swift
rm -rf "$fixtures" "${sentinel:h}"
echo "overall status $rc"; exit $rc
