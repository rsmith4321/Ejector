from pathlib import Path
import subprocess,uuid,plistlib,os,sys
repo=Path(__file__).resolve().parents[1]
os.chdir(repo)
evidence=Path(sys.argv[1]).resolve()
evidence.mkdir(parents=True, exist_ok=True)
work=evidence/('ee-notification-integration-'+str(uuid.uuid4()));work.mkdir()
app=work/'Notification Integration Tests.app'; mac=app/'Contents/MacOS';mac.mkdir(parents=True)
subprocess.run(['xcrun','swiftc',*['Ejector/'+x+'.swift' for x in ['DroneImportManager','MediaImportEngine','MetadataCleaner','ImportVolumes','ImportEjectPrompt','ImportCompletionNotification']],'Tests/ImportNotificationIntegrationTests.swift','-o',str(mac/'NotificationIntegrationTests')],check=True)
(app/'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier':'com.ryansmithphotography.EasyEject.notificationtests','CFBundleExecutable':'NotificationIntegrationTests','CFBundlePackageType':'APPL','LSUIElement':True}))
subprocess.run(['codesign','--force','--sign','Developer ID Application: Ryan Smith Photography, LLC (MCJMHBLT27)','--options','runtime',str(app)],check=True)
name='EE-Notification-Test-'+str(uuid.uuid4())[:8]
dmg=work/'fixture.dmg'
subprocess.run(['hdiutil','create','-size','32m','-fs','HFS+','-volname',name,str(dmg)],check=True,stdout=subprocess.DEVNULL)
mount=Path('/Volumes')/name
subprocess.run(['hdiutil','attach',str(dmg),'-nobrowse'],check=True,stdout=subprocess.DEVNULL)
try:
 with (evidence/'integration-tests.log').open('w') as log:
  proc=subprocess.run([str(mac/'NotificationIntegrationTests'),str(mount),str(work/'support')],stdout=log,stderr=subprocess.STDOUT,timeout=90)
  print('integration exit',proc.returncode)
  print((evidence/'integration-tests.log').read_text()[-3500:])
  proc.check_returncode()
finally:
 if mount.exists():subprocess.run(['hdiutil','detach',str(mount)],check=False)
