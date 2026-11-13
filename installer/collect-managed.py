"""Complete a native PlaybackBundle with managed dependency notices and sources.
Usage: python collect-managed.py project.assets.json nuget-packages PlaybackBundle
"""
import pathlib,sys,json,xml.etree.ElementTree as ET,shutil,hashlib,zipfile,urllib.request
assets_path,packages,out=map(pathlib.Path,sys.argv[1:4])
licenses=out/'licenses/managed';sources=out/'sources/managed'
licenses.mkdir(parents=True,exist_ok=True);sources.mkdir(parents=True,exist_ok=True)
manifest=[];repositories={}
for pkg,info in json.loads(assets_path.read_text(encoding='utf-8-sig'))['libraries'].items():
    if info['type']!='package':continue
    directory=packages/pkg.lower();ns=next(directory.glob('*.nuspec'))
    root=ET.parse(ns).getroot()
    tag=lambda s:root.tag.split('}')[0]+'}'+s if '}' in root.tag else s
    metadata=root.find(tag('metadata'));repo=metadata.find(tag('repository'));license_info=metadata.find(tag('license'))
    manifest.append(dict(package=pkg,license=license_info.text,repository=repo.attrib))
    target=licenses/pkg.replace('/','-');target.mkdir(exist_ok=True);shutil.copy2(ns,target/ns.name)
    found=False
    for f in directory.iterdir():
        if f.is_file() and any(n in f.name.lower() for n in ['license','notice']):shutil.copy2(f,target/f.name);found=True
    if not found:
        repository=repo.attrib['url'].removesuffix('.git');commit=repo.attrib['commit']
        # Upstream nuspec names the parent repository; this commit belongs to its C# submodule.
        if pkg.startswith('Std.UriTemplate/'):repository='https://github.com/std-uritemplate/std-uritemplate-csharp'
        if not repository.startswith('https://github.com/'):raise RuntimeError('Review source host: '+repository)
        repositories[(repository,commit)]=repository.rsplit('/',1)[1]
for (repository,commit),name in repositories.items():
    archive=sources/(name+'-'+commit+'.zip')
    if not archive.exists():urllib.request.urlretrieve(repository.replace('https://github.com/','https://codeload.github.com/')+'/zip/'+commit,archive)
    with zipfile.ZipFile(archive) as z:
        count=0
        for n in z.namelist():
            if pathlib.PurePosixPath(n).name.lower() in ['license','license.txt','license.md','notice','notice.txt','copying','third-party-notices.txt']:
                target=licenses/(name+'-'+n.split('/',1)[-1].replace('/','_'));target.write_bytes(z.read(n));count+=1
        if not count:raise RuntimeError('No license found for '+name)
(licenses/'packages.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
files=[dict(path=str(f.relative_to(out)),sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for f in out.rglob('*') if f.is_file() and f.name!='playback-manifest.json']
(out/'licenses/playback-manifest.json').write_text(json.dumps(files,indent=2),encoding='utf-8')
print('Collected',len(manifest),'managed package notices; bundle manifest updated')
