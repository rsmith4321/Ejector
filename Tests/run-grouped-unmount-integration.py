from pathlib import Path
import subprocess,uuid,sys,plistlib
repo=Path(__file__).resolve().parents[1]
evidence=Path(sys.argv[1]).resolve();evidence.mkdir(parents=True,exist_ok=True)
sandbox = '--sandbox' in sys.argv[2:]
app = evidence/'Grouped Unmount Tests.app'
exe = app/'Contents/MacOS/GroupedUnmountTests' if sandbox else evidence/'grouped-integration'
exe.parent.mkdir(parents=True, exist_ok=True)
subprocess.run(['xcrun','swiftc',str(repo/'Ejector/ImportVolumes.swift'),str(repo/'Tests/GroupedUnmountIntegrationTests.swift'),'-o',str(exe)],check=True)
if sandbox:
 (app/'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier':'com.ryansmithphotography.EasyEject.groupedunmounttests','CFBundleExecutable':'GroupedUnmountTests','CFBundlePackageType':'APPL','LSUIElement':True}))
 subprocess.run(['codesign','--force','--sign','Developer ID Application: Ryan Smith Photography, LLC (MCJMHBLT27)','--options','runtime','--entitlements',str(repo/'Store/Store.entitlements'),str(app)],check=True)
images=[];mounts=[]
try:
 for n in range(2):
  name='EE-Group-Test-'+str(uuid.uuid4())[:8]
  image=evidence/(name+'.dmg');images.append(image)
  subprocess.run(['hdiutil','create','-size','16m','-fs','HFS+','-volname',name,str(image)],check=True,stdout=subprocess.DEVNULL)
  subprocess.run(['hdiutil','attach',str(image),'-nobrowse'],check=True,stdout=subprocess.DEVNULL)
  mounts.append('/Volumes/'+name)
 with (evidence/'integration.log').open('w') as log:
  subprocess.run([str(exe),*mounts],check=True,stdout=log,stderr=subprocess.STDOUT,timeout=45)
 print((evidence/'integration.log').read_text())
finally:
 for image in images:
  # Identify this exact test image, including after its volume has been unmounted.
  import plistlib
  info=plistlib.loads(subprocess.check_output(['hdiutil','info','-plist']))
  for item in info.get('images',[]):
   if item.get('image-path')==str(image):
    dev=next((x['dev-entry'] for x in item.get('system-entities',[]) if x.get('dev-entry')) ,None)
    if dev:subprocess.run(['hdiutil','detach',dev],check=False,stdout=subprocess.DEVNULL)
