"""Export an immutable old core and compile it with the current benchmark harness."""
from pathlib import Path
import json, shutil, subprocess, sys, tarfile

root=Path(__file__).resolve().parents[2]
revision=sys.argv[1] if len(sys.argv)>1 else 'fda9f49'
target=(root/(sys.argv[2] if len(sys.argv)>2 else '.tmp/perf-baseline-fda9f49')).resolve()
if not target.is_relative_to((root/'.tmp').resolve()):raise SystemExit('Baseline destination must be within repository .tmp.')
if target.exists():raise SystemExit('Destination exists; use a new .tmp directory to preserve prior evidence.')
commit=subprocess.check_output(['git','rev-parse',revision],cwd=root,text=True).strip()
target.mkdir(parents=True)
archive=target.parent/(target.name+'.tar')
with archive.open('wb') as f:
    subprocess.run(['git','archive',commit,'Directory.Build.props','global.json','src/CRules','src/CRules.Abstractions'],cwd=root,stdout=f,check=True)
with tarfile.open(archive) as f:f.extractall(target,filter='data')
props=target/'Directory.Build.props'
props.write_text(props.read_text(encoding='utf-8-sig').replace('<TargetFramework>','<CRulesBaseline>true</CRulesBaseline>\n    <TargetFramework>',1),encoding='utf-8',newline='\n')
source=root/'benchmarks/CRules.Benchmarks';dest=target/'benchmarks/CRules.Benchmarks';dest.mkdir(parents=True)
for file in source.iterdir():
    if file.is_file():shutil.copy2(file,dest/file.name)
project=dest/'CRules.Benchmarks.csproj'
renamed=dest/'CRules.Benchmarks.Before.csproj'
project.rename(renamed)
renamed.write_text(renamed.read_text(encoding='utf-8-sig').replace('<OutputType>Exe</OutputType>','<AssemblyName>CRules.Benchmarks.Before</AssemblyName>\n    <OutputType>Exe</OutputType>'),encoding='utf-8',newline='\n')
(target/'baseline-source.json').write_text(json.dumps({'coreCommit':commit,'harness':'current workspace, identical source in both builds; API bridge uses CRULES_BASELINE'},indent=2)+'\n',encoding='utf-8',newline='\n')
print(target)
