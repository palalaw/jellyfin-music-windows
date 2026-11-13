import pathlib,sys,json,hashlib,os
p=pathlib.Path(sys.argv[1]);issues=[]
for name in ['connection.bin','preferences.json','userprofile.json']:
 if list(p.rglob(name)):issues.append('Unexpected user data: '+name)
if list(p.rglob('*.dmp')):issues.append('Crash dump included')
for source in (p/'sources/jellyfin-windows-player').rglob('*'):
 if source.is_file() and source.suffix.lower() in ['.cs','.ps1','.md','.txt','.json','.iss','.xaml']:
  if os.environ.get('JFM_FORBIDDEN_SERVER_HOST') and os.environ['JFM_FORBIDDEN_SERVER_HOST'].encode() in source.read_bytes():issues.append('Test endpoint found: '+str(source.relative_to(p)))
for rel in ['libmpv-2.dll','licenses/PLAYBACK-NOTICES.md','licenses/managed/packages.json','sources/playback/sources.json','sources/playback/build-native.ps1']:
 if not (p/rel).is_file():issues.append('Missing: '+rel)
sources=p/'sources/playback/sources.json'
if sources.exists():
 for item in json.loads(sources.read_text(encoding='utf-8-sig')):
  f=sources.parent/(item['name']+'.zip')
  if not f.exists() or hashlib.sha256(f.read_bytes()).hexdigest()!=item['sha256']:issues.append('Source hash: '+item['name'])
print(json.dumps(dict(passed=not issues,issues=issues),indent=2));sys.exit(bool(issues))

