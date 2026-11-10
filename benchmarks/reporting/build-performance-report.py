from pathlib import Path
import csv, json, math, statistics as stats, sys, hashlib

root=Path(__file__).resolve().parents[2]
folder=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'docs/data/performance/2026-11-10'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
rows=load(folder/'measurements.json'); comp=load(folder/'compilation.json')
run=load(folder/'run-manifest.json'); inputs=load(folder/'business-100k/manifest.json')
assert len(rows)==141 and len(comp)==50
assert all(r['Count']==100000 and r['WarmupCount']>=2000 and r['WarmupMs']>=750 for r in rows)
med=lambda values:stats.median(list(values))

def paired(before,after):
    before={r['Round']:r for r in before};after={r['Round']:r for r in after}
    assert before.keys()==after.keys() and len(before)==5
    ratios=[after[i]['ElapsedMs']/before[i]['ElapsedMs'] for i in sorted(before)]
    logs=list(map(math.log,ratios)); margin=2.7764451051977987*stats.stdev(logs)/math.sqrt(5)
    lo,hi=math.exp(stats.mean(logs)-margin),math.exp(stats.mean(logs)+margin)
    return dict(changePct=(med(ratios)-1)*100,ciLow=lo,ciHigh=hi,pairedRatios=ratios,
                status='regression' if lo>1.05 else 'uncertain' if hi>1.05 else 'not_regressed')

def aggregate(items):
    items=list(items)
    assert items
    return dict(ms=med(r['ElapsedMs'] for r in items),bytes=med(r['AllocatedBytes']/r['Count'] for r in items),
                minMs=min(r['ElapsedMs'] for r in items),maxMs=max(r['ElapsedMs'] for r in items),rounds=len(items))

short=['小 context / 简单','小 context / 100 变量','小 context / 100 变量 + 分支','大 context / 简单','大 context / 100 变量 + 分支']
scenarios=[]
for index,source in enumerate(inputs['Scenarios']):
    sid=source['Id'];selected=[r for r in rows if r['Scenario']==sid]
    valid=[r for r in selected if r['Mode']!='BuildOnly']
    assert len({r['ResultChecksum'] for r in valid})==1 and len({r['ContextChecksum'] for r in valid})==1
    assert max(abs(source['ExpectedMin']),abs(source['ExpectedMax']))<2**53
    before=[r for r in selected if r['Version']=='before'];after=[r for r in selected if r['Version']=='current' and r['Mode']=='CRules']
    modes={m:aggregate(r for r in selected if r['Version']=='current' and r['Mode']==m) for m in ['CRules','NativeEquivalent','NativeReference','JintClr','JintSingleton','JintJson']}
    assert all(modes[m]['rounds']==(5 if m in ['CRules','NativeEquivalent'] else 3) for m in modes)
    regression=paired(before,after);regression.update(beforeMs=aggregate(before)['ms'],afterMs=modes['CRules']['ms'],beforeBytes=aggregate(before)['bytes'])
    cb=[r for r in comp if r['Scenario']==sid and r['Version']=='before'];ca=[r for r in comp if r['Scenario']==sid and r['Version']=='current']
    assert len({r['SourceSha256'] for r in cb+ca})==1
    source_file=folder/'business-100k/rules'/f'{sid}.clr.txt'
    assert hashlib.sha256(source_file.read_bytes()).hexdigest().upper()==source['SourceSha256']
    assert cb[0]['SourceSha256']==source.get('RuntimeSourceSha256',source['SourceSha256'])
    compilation=paired(cb,ca);compilation.update(beforeMs=med(r['ElapsedMs']/r['Count'] for r in cb),afterMs=med(r['ElapsedMs']/r['Count'] for r in ca),
        count=cb[0]['Count'],beforeBytes=med(r['AllocatedBytes']/r['Count'] for r in cb),afterBytes=med(r['AllocatedBytes']/r['Count'] for r in ca))
    build=aggregate(r for r in rows if r['Scenario']==('S4' if source['Large'] else 'S1') and r['Mode']=='BuildOnly')
    scenarios.append(dict(id=sid,name=source['Name'],shortName=short[index],large=source['Large'],locals=source['Locals'],conditions=source['Conditions'],
        jsonBytes=source['AverageContextJsonBytes'],modes=modes,regression=regression,compilation=compilation,build=build,
        rule=(folder/'business-100k/rules'/f'{sid}.clr.txt').read_text(encoding='utf-8-sig')))

def bdn(version):
    results={}
    for file in (folder/'bdn'/version).rglob('*-report-full*.json'):
        for row in load(file).get('Benchmarks',[]):
            if row.get('Statistics') is None: raise AssertionError(f'Incomplete BDN row: {file}: {row}')
            typ=row.get('Type',row.get('FullName','').split('.')[-2]); method=row.get('Method',''); parameters=row.get('Parameters','')
            key=(typ,method,parameters)
            mean=row['Statistics']['Mean'];sd=row['Statistics']['StandardDeviation']; memory=row.get('Memory',{}) or {}
            results[key]=dict(mean=mean,sd=sd,bytes=memory.get('BytesAllocatedPerOperation',0),type=typ,method=method,parameters=parameters)
    return results
before_bdn=bdn('before');current_bdn=bdn('current')
assembly_proof=load(folder/'core-il-proof.json')
for version in ['before','current']:
    provenance=[load(p) for p in (folder/'bdn'/version/'provenance').glob('*.json')]
    assert provenance, f'Missing actual BDN assembly provenance: {version}'
    assert assembly_proof[version]['businessBinarySha256']==run['assemblies'][version]['CRules.dll'].lower()
    allowed=assembly_proof[version]['bdnBinarySha256']
    assert all(p['CoreSha256'].lower() in allowed for p in provenance), f'Wrong core loaded by BDN: {version}'
micro=[];api=[];pure=[]
api_names={'TypedContext':'强类型 · 单上下文','RuntimeTypeContext':'运行时 Type · 单上下文','TypedNamedInputs':'强类型 · 两个命名 decimal 输入','RuntimeTypeNamedInputs':'运行时 Type · 两个命名 decimal 输入'}
type_names={'RoutingBenchmarks':'整数路由','AccountingBenchmarks':'decimal 计算','CollectionBenchmarks':'集合过滤求和','CompilationBenchmarks':'准备阶段','HostLifecycleBenchmarks':'JSON 宿主入口','BusinessRuleHotBenchmarks':'纯规则（预分配输入）'}
method_names={'NativeDirect':'原生直接调用（禁用内联）','NativeDelegate':'原生委托','NativeLinq':'原生 LINQ',
    'CRulesHot':'CRules 强类型委托','JintHot':'Jint 预建 JS 对象','JintDecimalHelpers':'Jint 十进制字符串 helper',
    'ClrParseAuditCompile':'CRules 解析、审计与委托编译','JintPrepareAst':'Jint 仅准备 AST',
    'JintPrepareCreateExecute':'Jint 解析、新 Engine 与执行','ClrJsonSnapshot':'CRules JSON 输入、执行与输出',
    'JintFreshEngineJson':'Jint 新 Engine、JSON 与冻结','JintCreateAndDispose':'Jint 仅创建并释放 Engine','NativeEquivalent':'原生同构委托'}
for key,row in sorted(current_bdn.items()):
    label=f'{type_names.get(row["type"],row["type"])} / {method_names.get(row["method"],row["method"])}'+(' · '+row['parameters'] if row['parameters'] else '')
    value=dict(label=label,mean=row['mean'],sd=row['sd'],bytes=row['bytes'],beforeMean=before_bdn.get(key,{}).get('mean'))
    if row['type']=='InvocationApiBenchmarks':value['label']=api_names[row['method']];api.append(value)
    elif row['type']=='BusinessRuleHotBenchmarks':continue
    else:
        value['engine']='Jint' if row['method'].startswith('Jint') else 'CRules' if row['method'].startswith(('CRules','Clr')) else '原生 C#'
        micro.append(value)
assert len(before_bdn)==31 and len(current_bdn)==35, (len(before_bdn),len(current_bdn))
def method_result(data,typ,method):return next(r for r in data.values() if r['type']==typ and r['method']==method)
small_before=method_result(before_bdn,'CompilationBenchmarks','ClrParseAuditCompile');small_after=method_result(current_bdn,'CompilationBenchmarks','ClrParseAuditCompile')
compile_probe=dict(beforeMs=small_before['mean']/1e6,afterMs=small_after['mean']/1e6,changePct=(small_after['mean']/small_before['mean']-1)*100)
decimal_before=method_result(before_bdn,'AccountingBenchmarks','CRulesHot');decimal_after=method_result(current_bdn,'AccountingBenchmarks','CRulesHot')
native_before=method_result(before_bdn,'AccountingBenchmarks','NativeDelegate');native_after=method_result(current_bdn,'AccountingBenchmarks','NativeDelegate')
decimal_change=(decimal_after['mean']/decimal_before['mean']-1)*100
native_change=(native_after['mean']/native_before['mean']-1)*100
decimal_normalized=((decimal_after['mean']/native_after['mean'])/(decimal_before['mean']/native_before['mean'])-1)*100
decimal_note=f"decimal 强类型热调用观测为 {decimal_before['mean']:.2f} → {decimal_after['mean']:.2f} ns（{decimal_change:+.1f}%）；原生委托同期 {native_change:+.1f}%，相对原生归一化为 {decimal_normalized:+.1f}%。基础组只有 3 次测量，这是一项需保留的变化信号，不能直接归因为重构。"
for sid in ['S1','S2','S3','S5']:
    def find(data,method):return next(r for r in data.values() if r['type']=='BusinessRuleHotBenchmarks' and r['method']==method and sid in r['parameters'])
    cb=find(before_bdn,'CRulesHot');ca=find(current_bdn,'CRulesHot');nb=find(before_bdn,'NativeEquivalent');na=find(current_bdn,'NativeEquivalent')
    pure.append(dict(id=sid,beforeNs=cb['mean'],afterNs=ca['mean'],beforeSd=cb['sd'],afterSd=ca['sd'],
        nativeBeforeNs=nb['mean'],nativeAfterNs=na['mean'],nativeBeforeSd=nb['sd'],nativeAfterSd=na['sd'],
        baselineUnstable=nb['sd']/nb['mean']>.1 or na['sd']/na['mean']>.1,changePct=(ca['mean']/cb['mean']-1)*100,
        normalizedChangePct=((ca['mean']/na['mean'])/(cb['mean']/nb['mean'])-1)*100,bytes=ca['bytes']))
regressed=[s['id'] for s in scenarios if s['regression']['status']=='regression'];uncertain=[s['id'] for s in scenarios if s['regression']['status']=='uncertain']
hot='确认发生超过 5% 的热执行回退：'+', '.join(regressed)+'。' if regressed else '本轮五种已有规则未确认超过 5% 的强类型热执行回退，但这不等于证明零回退。'
if uncertain:hot+=' '+', '.join(uncertain)+' 的区间仍覆盖 +5%，测量不确定性需要保留。'
if any(p['baselineUnstable'] for p in pure):hot+=' 补充纯调用中部分原生对照也明显漂移，相关归一化值不能用来断言重构导致回退或提升。'
changes=[s['compilation']['changePct'] for s in scenarios]
compile_text=f"编译准备的配对中位变化为 {min(changes):+.1f}%～{max(changes):+.1f}%。短规则 BDN 编译微基准为 {compile_probe['beforeMs']:.3f} → {compile_probe['afterMs']:.3f} ms（{compile_probe['changePct']:+.1f}%），存在准备成本增长。这部分属于加载或规则变更成本，不能用热执行优势掩盖；应编译后复用。"
cpu=load(folder/'cpu.json')
environment=f"{cpu['Name'].strip()} · {cpu['NumberOfCores']} 核 / {cpu['NumberOfLogicalProcessors']} 线程 · Windows 10.0.26200 x64 · SDK 10.0.401 / .NET 10.0.12 · DynamicExpresso.Core 2.19.6 · Jint 4.17.0 · BenchmarkDotNet 0.15.8。"
html_rows=[dict(r,ResultChecksum=str(r['ResultChecksum']),ContextChecksum=str(r['ContextChecksum'])) for r in rows]
dataset=dict(scenarios=scenarios,hotConclusion=hot,compileConclusion=compile_text,compileProbe=compile_probe,decimalObservation=decimal_note,micro=micro,api=api,pure=pure,raw=html_rows,environment=environment,manifest=run)
payload=json.dumps(dataset,ensure_ascii=False,separators=(',',':')).replace('</','<\\/')
(folder/'summary.json').write_text(json.dumps(dataset,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
with (folder/'measurements.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=rows[0].keys());writer.writeheader();writer.writerows(rows)
print(hot);print(compile_text)
for s in scenarios:print(s['id'],s['modes']['CRules']['ms'],s['regression']['changePct'],s['compilation']['changePct'])

fmt=lambda value,d=2:f'{value:,.{d}f}'
pct=lambda value:f'{value:+.2f}%'
lines=['# CRules、Jint 与原生 C# 性能复测','',
    '测试日期：2026-11-10。实际重跑重构前 `fda9f49` 与当前核心 `99dda85`，使用相同 benchmark 工具、输入和依赖。仅保留文字分析和 JSON/CSV 原始数据。','',
    '## 本轮结论','',hot,'',compile_text,'',
    '回退判断覆盖既有规则的强类型调用路径；新增运行时 Type 入口单独测量。NativeAOT、动态 ExpandoObject、异步等待和所有新语法组合未在本轮建立完整的性能基线。','',
    '## CRules / Jint / 原生 C#：完整循环','',
    '以下单位为 ms / 100,000 次。全部包含每次构建随机 context、传参、计算、逐项结果校验；不包含规则准备。CRules 与原生同构方法各取 5 个独立进程的中位数，Jint 各入口取 3 个进程的中位数。','',
    '| 场景 | CRules 强类型 | Jint 函数 + CLR | Jint SetValue | Jint 函数 + JSON | 原生 C# 同构委托 | Jint CLR / CRules |',
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
for s in scenarios:
    m=s['modes'];lines.append(f"| {s['id']} {s['shortName']} | {fmt(m['CRules']['ms'])} | {fmt(m['JintClr']['ms'])} | {fmt(m['JintSingleton']['ms'])} | {fmt(m['JintJson']['ms'])} | {fmt(m['NativeEquivalent']['ms'])} | {m['JintClr']['ms']/m['CRules']['ms']:.2f}× |")
lines+=['','CRules：准备一次后调用强类型委托。Jint：每个进程仅创建一个 Engine，分别使用 CLR 对象函数调用、SetValue + 已解析 Evaluate、JSON 转换函数调用，入口没有混合。原生 C#：相同规则展开成静态方法，禁止内联，通过 Func 委托调用。独立循环参考算法只用于期望值校验，不能代表原生 C# 性能上界。','',
    '主测使用安全整数/定点语义，没有额外给 Jint 叠加 decimal helper 成本；这不表示任意 decimal 算法可直接替换为 JS Number。','',
    '## 重构前后：配对热执行','',
    '| 场景 | 重构前 ms | 重构后 ms | 同轮比值中位变化 | 比值 95% 区间 | 判断 |',
    '| --- | ---: | ---: | ---: | ---: | --- |']
labels={'regression':'确认 >5% 回退','uncertain':'区间仍覆盖 >5%，待复核','not_regressed':'未确认 >5% 回退'}
for s in scenarios:
    r=s['regression'];lines.append(f"| {s['id']} | {fmt(r['beforeMs'])} | {fmt(r['afterMs'])} | {pct(r['changePct'])} | {r['ciLow']:.3f}–{r['ciHigh']:.3f}× | {labels[r['status']]} |")
lines+=['','正值表示变慢。变化点值是每轮耗时比值的中位数，不是两列中位数相除；95% 区间基于 5 个配对 log 比值的 t 区间（df=4）。只有区间下界超过 1.05 才标记为确认回退。样本有限，区间较宽时保留不确定性。','',
    '### 预分配输入：只测规则调用','',
    '为避免 context 构建掩盖细小变化，补测相同展开算法的纯规则委托。各版本、各方法使用 3 个独立进程，每进程 3 次预热和 5 次测量。固定输入的分支预测与随机输入不同，不与完整循环合并。','',
    '| 场景 | CRules 前 ns | CRules 后 ns | 后 StdDev ns | 原生前 ns | 原生后 ns | CRules 变化 | 相对原生归一化变化 | 后 B/op |',
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
for p in pure:lines.append(f"| {p['id']} | {fmt(p['beforeNs'],3)} | {fmt(p['afterNs'],3)} | {fmt(p['afterSd'],3)} | {fmt(p['nativeBeforeNs'],3)} | {fmt(p['nativeAfterNs'],3)} | {pct(p['changePct'])} | {pct(p['normalizedChangePct']) if not p['baselineUnstable'] else '对照不稳定，不用于归因'} | {fmt(p['bytes'],1)} |")
lines+=['','归一化值为 `(CRules后 / 原生后) / (CRules前 / 原生前) - 1`，用于观察机器速度变化是否影响两条路径；它不能消除所有噪声，也不能取代原始均值与标准差。','',
    '## 编译准备成本','',
    '复用同一编译器和固定源码，预热 5 次后重复编译为强类型委托。排除源码生成、context 构建与执行。各版本各场景 5 个独立进程，简单规则每进程 200 次，复杂规则 20 次。','',
    '| 场景 | 次数/进程 | 重构前 ms/编译 | 重构后 ms/编译 | 配对变化 | 前 KiB/编译 | 后 KiB/编译 |',
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
for s in scenarios:
    c=s['compilation'];lines.append(f"| {s['id']} | {c['count']} | {fmt(c['beforeMs'],3)} | {fmt(c['afterMs'],3)} | {pct(c['changePct'])} | {fmt(c['beforeBytes']/1024)} | {fmt(c['afterBytes']/1024)} |")
lines+=['','CRules 的编译包括绑定、审计、表达式树组合和委托/通用调用适配器生成。Jint AST 准备产物不同，在微基准中独立列出，不把两者称为同等工作量。规则应在加载或变化时准备并复用。','',
    '## 分配与输入规模','',
    '| 场景 | 仅 context B/次 | CRules 前 B/次 | CRules 后 B/次 | 原生同构 B/次 | Jint CLR B/次 | Jint JSON B/次 |',
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
for s in scenarios:
    m=s['modes'];lines.append(f"| {s['id']} | {fmt(s['build']['bytes'])} | {fmt(s['regression']['beforeBytes'])} | {fmt(m['CRules']['bytes'])} | {fmt(m['NativeEquivalent']['bytes'])} | {fmt(m['JintClr']['bytes'])} | {fmt(m['JintJson']['bytes'])} |")
lines+=['','这些是包含 context 的完整循环托管分配，不是单独引擎大小。小 context 约 268 B JSON 样本、384 B 实际构造分配；大 context 约 11.7 KiB JSON 样本、17,304 B 构造分配，包含数组、64 个明细、标签、字典和每次新建的 4096 字符 payload。峰值工作集还包括启动、准备和预热，原始记录保留 GC 与工作集，不能直接作为引擎常驻大小。','',
    '## BenchmarkDotNet 微基准','',
    'ShortRun：1 个 launch、3 个 warmup、3 个 measurement。下表为 ns/op，分配为 B/op。固定输入/预分配对象，与完整循环口径不同。只有 3 个测量迭代，纳秒级差异不用于宣称超越原生 C#。','',
    '| 场景与方法 | 实现 | 前 Mean ns | 当前 Mean ns | 当前 StdDev ns | 当前 B/op |',
    '| --- | --- | ---: | ---: | ---: | ---: |']
for m in micro:lines.append(f"| {m['label']} | {m['engine']} | {fmt(m['beforeMean'],3) if m['beforeMean'] is not None else '—'} | {fmt(m['mean'],3)} | {fmt(m['sd'],3)} | {fmt(m['bytes'],1)} |")
lines+=['',decimal_note,'','decimal 场景中的 Jint 使用十进制字符串 helper，包含转换和跨边界调用；普通整数路由用预构造 JS 对象。集合 CRules 使用 AllowRuntimeLambdaBinding 实验模式，需保留它与原生 LINQ 的时间和分配差距。宿主测试中的 Jint 每次新 Engine 包含 JSON/冻结，与复用入口分开。','',
    '## CRules 新调用入口','',
    '| 入口 | Mean ns/op | StdDev ns/op | B/op |','| --- | ---: | ---: | ---: |']
for m in api:lines.append(f"| {m['label']} | {fmt(m['mean'],3)} | {fmt(m['sd'],3)} | {fmt(m['bytes'],1)} |")
lines+=['','强类型入口直接调用具体委托。运行时 Type 入口包括 params 数组、参数检查、值类型装箱及通用调用适配；不能把强类型委托的零分配结论套用于它。新入口没有重构前同名基线。','',
    '## 环境、验证与复现','',environment,'',
    '完整循环与编译探针固定逻辑核心 2（affinity mask 4），统一设置 DOTNET_TieredCompilation=0；各执行进程至少预热 2,000 次且不少于 750 ms。BDN 使用相同核心、默认 JIT 策略及自身预热。两类计时不混合。最初默认调度/分层编译的预检样本整体弃用，没有挑选其中更有利的轮次进入正式结果。','',
    '固定随机种子 20261109。141 条完整循环记录，其中 135 条计算记录共 13,500,000 次逐项校验，另有 6 条构建基线；50 条编译记录。旧/新实际 BDN 工作进程加载的核心 IL/签名指纹分别与对应版本一致，排除了误测同一份核心。BDN 重建可能改变 MVID/PDB 路径，因此二进制 SHA 也保留，但不要求与直接构建完全相同。','',
    '本目录原始数据：[执行记录](measurements.json)、[CSV](measurements.csv)、[编译记录](compilation.json)、[汇总及区间](summary.json)、[版本与程序集哈希](run-manifest.json)。完整复现步骤见仓库 benchmarks/README.md。','',
    '仅代表该轮测量版本；不能把跨批次数值变化直接归因于代码回退。该性能测量不包含 NativeAOT。','']
(folder/'generated-report.md').write_text('\n'.join(lines),encoding='utf-8',newline='\n')
