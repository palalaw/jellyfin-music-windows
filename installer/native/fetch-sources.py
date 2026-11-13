import pathlib,sys,json,urllib.request,zipfile,hashlib,concurrent.futures,shutil
root=pathlib.Path(sys.argv[1]).resolve();root.mkdir(parents=True,exist_ok=True)
manifest=pathlib.Path(__file__).with_name('sources.json')
items=json.loads(manifest.read_text(encoding='utf-8-sig'))
def fetch(item):
 archive=root/(item['name']+'.zip')
 if not archive.exists(): urllib.request.urlretrieve(item['url'],archive)
 if hashlib.sha256(archive.read_bytes()).hexdigest()!=item['sha256']: raise RuntimeError('Source checksum mismatch: '+item['name'])
 destination=root/item['name']
 if not destination.exists():
  with zipfile.ZipFile(archive) as z:
   top=z.namelist()[0].split('/')[0]
   for n in z.namelist():
    if not (root/n).resolve().is_relative_to(root): raise RuntimeError('Unsafe archive path')
   z.extractall(root)
  (root/top).rename(destination)
 return item['name']
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor: print(list(executor.map(fetch,items)))
shutil.copytree(root/'fast_float',root/'libplacebo/3rdparty/fast_float',dirs_exist_ok=True)
shutil.copytree(root/'vulkan-headers',root/'libplacebo/3rdparty/Vulkan-Headers',dirs_exist_ok=True)
shutil.copy2(manifest,root/'sources.json')
