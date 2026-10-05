from pathlib import Path
import subprocess,uuid,plistlib,os,sys
repo=Path(__file__).resolve().parents[1]
os.chdir(repo)
evidence=Path(sys.argv[1]).resolve()
evidence.mkdir(parents=True, exist_ok=True)
work=evidence/('ee-quick-share-integration-'+str(uuid.uuid4()));work.mkdir()
app=work/'Manual Quick Share Tests.app'; mac=app/'Contents/MacOS';mac.mkdir(parents=True)
subprocess.run(['xcrun','swiftc',*['Ejector/'+x+'.swift' for x in ['DroneProfile','ImportDefaults','DroneImportManager','MediaImportEngine','MetadataCleaner','ImportVolumes','ImportEjectPrompt','ImportCompletionNotification','EasyShare','QuickShareImport','QuickSharePrompt']],'Tests/ManualQuickShareIntegrationTests.swift','-o',str(mac/'ManualQuickShareIntegrationTests')],check=True)
(app/'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier':'com.ryansmithphotography.EasyEject.manualquicksharetests','CFBundleExecutable':'ManualQuickShareIntegrationTests','CFBundlePackageType':'APPL','LSUIElement':True}))
subprocess.run(['codesign','--force','--sign','Developer ID Application: Ryan Smith Photography, LLC (MCJMHBLT27)','--options','runtime',str(app)],check=True)
name='EE-QuickShare-Test-'+str(uuid.uuid4())[:8]
dmg=work/'fixture.dmg'
subprocess.run(['hdiutil','create','-size','32m','-fs','HFS+','-volname',name,str(dmg)],check=True,stdout=subprocess.DEVNULL)
mount=Path('/Volumes')/name
subprocess.run(['hdiutil','attach',str(dmg),'-nobrowse'],check=True,stdout=subprocess.DEVNULL)
try:
 (mount/'DCIM').mkdir()
 video=mount/'DCIM/VID_generated.mp4'
 subprocess.run(['/opt/homebrew/bin/ffmpeg','-nostdin','-v','error','-f','lavfi','-i','testsrc2=size=640x360:rate=30','-f','lavfi','-i','sine=frequency=400:sample_rate=48000','-t','1','-c:v','libx264','-preset','ultrafast','-c:a','aac',str(video)],check=True)
 with video.open('ab') as f:f.write(b'Insta360 Luna Ultra')
 with (evidence/'manual-integration-tests.log').open('w') as log:
  proc=subprocess.run([str(mac/'ManualQuickShareIntegrationTests'),str(mount),str(work/'support')],stdout=log,stderr=subprocess.STDOUT,timeout=90)
  print('integration exit',proc.returncode)
  print((evidence/'manual-integration-tests.log').read_text()[-3500:])
  proc.check_returncode()
finally:
 if mount.exists():subprocess.run(['hdiutil','detach',str(mount)],check=False)
