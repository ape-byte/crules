"""Sequential same-machine measurements. Never run two timed workers concurrently."""
from pathlib import Path
from datetime import datetime, timezone, timedelta
from hashlib import sha256
import json, subprocess, sys, time, platform, os, ctypes

baseline, current, output = [Path(p).resolve() for p in sys.argv[1:4]]
output.mkdir(parents=True, exist_ok=True)
workers = output / 'workers'
workers.mkdir(exist_ok=True)
data = output / 'business-100k'
flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
records, compilation = [], []

def write(name, value):
    (output / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')

def run(exe, args, log=None):
    environment=dict(os.environ,DOTNET_TieredCompilation='0')
    with subprocess.Popen([str(exe), *map(str, args)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding='utf-8', errors='replace', creationflags=flags,env=environment) as process:
        if os.name=='nt':
            kernel=ctypes.WinDLL('kernel32',use_last_error=True)
            kernel.OpenProcess.argtypes=[ctypes.c_uint,ctypes.c_bool,ctypes.c_uint];kernel.OpenProcess.restype=ctypes.c_void_p
            kernel.SetProcessAffinityMask.argtypes=[ctypes.c_void_p,ctypes.c_size_t];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
            handle=kernel.OpenProcess(0x200,False,process.pid)
            if not handle: raise ctypes.WinError(ctypes.get_last_error())
            try:
                if not kernel.SetProcessAffinityMask(handle,4):raise ctypes.WinError(ctypes.get_last_error())
            finally:kernel.CloseHandle(handle)
        text,_=process.communicate()
        if log: Path(log).write_text(text, encoding='utf-8', newline='\n')
        if process.returncode: raise RuntimeError(f'{exe.name} {args}:\n{text[-8000:]}')
        return text

write('run-manifest.json', {
    'date': datetime.now(timezone(timedelta(hours=8))).isoformat(), 'baselineCommit': 'fda9f49', 'currentCommit': '99dda85',
    'harness': 'Identical source in both builds, except compile-time old/new public API bridge; core sources unchanged.',
    'count': 100000, 'seed': 20261109, 'crulesPairedRounds': 5, 'jintRounds': 3, 'warmupMinimumIterations': 2000, 'warmupMinimumMs': 750,
    'excluded': 'Per-request new Jint Engine is measured separately by HostLifecycleBenchmarks, not mixed with reuse throughput.',
    'platform': platform.platform(),
    'workerJitPolicy': {'DOTNET_TieredCompilation':'0','affinityMask':4,'logicalProcessor':2},
    'protocolNote':'Preflight samples with default scheduling/tiering were discarded as a whole; fixed-core fully optimized JIT is used for every final business worker. BDN uses default JIT with fixed affinity separately.',
    'assemblies': {label: {p.name: sha256(p.read_bytes()).hexdigest() for p in exe.parent.glob('*.dll') if p.name.startswith(('CRules', 'Jint', 'DynamicExpresso'))} for label, exe in [('before', baseline), ('current', current)]}
})

started = time.monotonic()
for round_id in range(1, 6):
    for scenario in range(1, 6):
        sid = f'S{scenario}'
        jobs = [('before','CRules',baseline),('current','CRules',current),('current','NativeEquivalent',current)]
        if round_id <= 3:
            jobs += [('current', m, current) for m in ('NativeReference','JintClr','JintJson','JintSingleton')]
            if scenario in (1, 4): jobs += [('current','BuildOnly',current)]
        rotate = (round_id + scenario - 2) % len(jobs)
        for version, mode, exe in jobs[rotate:] + jobs[:rotate]:
            file = workers / f'{sid}-{version}-{mode}-{round_id}.json'
            print(f'RUN {sid} {version} {mode} round={round_id}', flush=True)
            run(exe, ['--business-worker',sid,mode,data / f'{sid}.expected.bin',file,100000,20261109,round_id])
            row = json.loads(file.read_text(encoding='utf-8-sig'))
            row['Version'] = version
            records.append(row)
            write('measurements.partial.json', records)
            print(f'DONE {row["ElapsedMs"]:.2f} ms; {row["AllocatedBytes"]/row["Count"]:.2f} B/op; elapsed={time.monotonic()-started:.0f}s', flush=True)
write('measurements.json', records)

for round_id in range(1, 6):
    for scenario in range(1, 6):
        sid = f'S{scenario}'; count = 200 if scenario in (1, 4) else 20
        jobs = [('before',baseline),('current',current)]
        if (round_id + scenario) % 2: jobs.reverse()
        for version, exe in jobs:
            file = workers / f'compile-{sid}-{version}-{round_id}.json'
            print(f'COMPILE {sid} {version} round={round_id} count={count}', flush=True)
            run(exe,['--compile-worker',sid,count,file,round_id])
            row=json.loads(file.read_text(encoding='utf-8-sig'));row['Version']=version;compilation.append(row)
            write('compilation.partial.json',compilation)
            print(f'DONE {row["ElapsedMs"]/count:.3f} ms/compile; {row["AllocatedBytes"]/count:.0f} B/compile',flush=True)
write('compilation.json', compilation)
for sid in [f'S{i}' for i in range(1,6)]:
    checked = [r for r in records if r['Scenario']==sid and r['Mode']!='BuildOnly']
    assert len({r['ResultChecksum'] for r in checked})==1, sid
    assert len({r['ContextChecksum'] for r in checked})==1, sid
    assert len({r['SourceSha256'] for r in compilation if r['Scenario']==sid})==1, sid
print(f'PASS rows={len(records)} compile_rows={len(compilation)} checked={sum(r["Count"] for r in records if r["Mode"]!="BuildOnly")}',flush=True)
