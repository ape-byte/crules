from pathlib import Path
from hashlib import sha256
import json, subprocess, sys

root=Path(__file__).resolve().parents[2]
before,current,output=[Path(a).resolve() for a in sys.argv[1:4]]
manifest=json.loads((output/'run-manifest.json').read_text())
tool=root/'benchmarks/reporting/CoreAssemblyProof/bin/Release/net10.0/CoreAssemblyProof.exe'
proof={}
for version,exe in [('before',before),('current',current)]:
    direct=exe.parent/'CRules.dll'
    assert sha256(direct.read_bytes()).hexdigest()==manifest['assemblies'][version]['CRules.dll']
    observed={}
    for file in (output/'bdn'/version/'provenance').glob('*.json'):
        data=json.loads(file.read_text());observed[data['CoreSha256'].lower()]=data['CorePath']
    assert observed,version
    paths=[str(direct),*observed.values()]
    result=subprocess.run([str(tool),*paths],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',check=True)
    metadata=json.loads(result.stdout)
    assert len({p['IlSignatureSha256'] for p in metadata})==1,version
    proof[version]={'businessBinarySha256':manifest['assemblies'][version]['CRules.dll'],
                    'ilSignatureSha256':metadata[0]['IlSignatureSha256'],'methodCount':metadata[0]['Methods'],
                    'bdnBinarySha256':list(observed),'observedPaths':list(observed.values())}
assert proof['before']['ilSignatureSha256']!=proof['current']['ilSignatureSha256']
proof['note']='BDN rebuilds may change MVID/PDB path and thus binary SHA-256. Emitted method bodies and signatures must match the corresponding directly built business core; old and current core fingerprints must differ.'
(output/'core-il-proof.json').write_text(json.dumps(proof,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print('All actual BDN core assemblies match the expected old/new IL and signature fingerprints.')
