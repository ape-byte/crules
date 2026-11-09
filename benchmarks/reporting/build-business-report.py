from pathlib import Path
import json, statistics, csv, hashlib, sys

root=Path(__file__).resolve().parents[2]
folder=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'docs/performance/2026-11-09/business-100k'
manifest=json.loads((folder/'manifest.json').read_text(encoding='utf-8-sig'))
if (folder/'cpu.json').exists():
    manifest['Machine']['Cpu']=json.loads((folder/'cpu.json').read_text(encoding='utf-8-sig'))
manifest['JsonSizeSampleCount']=16
manifest['MigrationNote']='程序集哈希对应实际被测构建产物；本次仅重命名项目标识，原始性能测量和输入校验结果保持不变。'
lock=json.loads((root/'benchmarks/CRules.Benchmarks/packages.lock.json').read_text(encoding='utf-8-sig'))
manifest['Dependencies']={name:lock['dependencies']['net10.0'][name]['resolved'] for name in ['Jint','DynamicExpresso.Core']}
if (folder/'singleton-supplement.json').exists():
    manifest['SingletonSupplement']=json.loads((folder/'singleton-supplement.json').read_text(encoding='utf-8-sig'))
results=json.loads((folder/'results.json').read_text(encoding='utf-8-sig'))
preparation=json.loads((folder/'preparation.json').read_text(encoding='utf-8-sig'))
assert len(results)==96
assert all(r['Count']==100000 for r in results)
for scenario in manifest['Scenarios']:
    rows=[r for r in results if r['Scenario']==scenario['Id'] and r['Mode']!='BuildOnly']
    assert len(rows)==18
    assert len({r['ResultChecksum'] for r in rows})==1
    assert len({r['ContextChecksum'] for r in rows})==1
    assert max(abs(scenario['ExpectedMin']),abs(scenario['ExpectedMax']))<2**53
assert len(preparation)==60
rules={s['Id']:{'clr':(folder/'rules'/f"{s['Id']}.clr.txt").read_bytes().decode('utf-8-sig'),
                 'js':(folder/'rules'/f"{s['Id']}.js.txt").read_bytes().decode('utf-8-sig'),
                 'context':(folder/'rules'/f"{s['Id']}.context.json").read_bytes().decode('utf-8-sig')}
       for s in manifest['Scenarios']}
for s in manifest['Scenarios']:
    src=rules[s['Id']]['clr']
    assert hashlib.sha256(src.encode()).hexdigest().upper()==s['SourceSha256']
    assert src.count('var v')==s['Locals']
    assert src.count('if (')==s['Conditions']
# JS cannot represent UInt64 checksums exactly. Keep their exact digits in strings in downloadable embedded data.
html_results=[dict(r,ResultChecksum=str(r['ResultChecksum']),ContextChecksum=str(r['ContextChecksum'])) for r in results]
dataset={'manifest':manifest,'results':html_results,'preparation':preparation,'rules':rules}
template=Path(__file__).with_name('business-report-template.html').read_text(encoding='utf-8-sig')
payload=json.dumps(dataset,ensure_ascii=False,separators=(',',':')).replace('</','<\\/')
html=template.replace('__DATASET__',payload)
assert '__DATASET__' not in html
(folder/'index.html').write_text(html,encoding='utf-8')
(folder/'report-data.json').write_text(json.dumps(dataset,ensure_ascii=False,indent=2),encoding='utf-8')
with (folder/'measurements.csv').open('w',newline='',encoding='utf-8-sig') as f:
    writer=csv.DictWriter(f,fieldnames=results[0].keys());writer.writeheader();writer.writerows(results)

def pick(s,m):
    if m=='BuildOnly':s='S4' if next(x for x in manifest['Scenarios'] if x['Id']==s)['Large'] else 'S1'
    return [r for r in results if r['Scenario']==s and r['Mode']==m]
def value(s,m,k):return statistics.median(r[k] for r in pick(s,m))
def number(x):return f'{x:,.2f}'
def mib(x):return x/1048576
lines=['# 随机新建 context 十万次计算对比','',
       '本次按每次新建随机 context 的完整循环补测。5 种场景、每种 6 个计算实现、每实现 3 个独立进程，每进程实测 100000 次；另有小／大 context 构建基线。共 96 条进程记录，9000000 次计算结果逐项校验一致。',
       '', '## 总耗时与倍率','',
       '以下均为三轮中位数，单位 ms。最佳 Jint 是三种单例入口（SetValue 加预解析 Evaluate、函数 CLR 参数、函数 JSON 参数）中耗时较低者。三者都只创建一个 Engine 连续执行十万次。隔离路径每次新建 Engine，另包含 JSON 与深冻结。', '',
       '| 场景 | CRules | 单例 SetValue | 单例函数 CLR | 单例函数 JSON | 最佳单例 Jint / CRules | Jint 隔离 |',
       '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
summary=[]
for s in manifest['Scenarios']:
    i=s['Id'];c=value(i,'CRules','ElapsedMs');jc=value(i,'JintClr','ElapsedMs');jj=value(i,'JintJson','ElapsedMs');isolated=value(i,'JintIsolatedJson','ElapsedMs')
    singleton=value(i,'JintSingleton','ElapsedMs'); best=min(['JintClr','JintJson','JintSingleton'],key=lambda mode:value(i,mode,'ElapsedMs'))
    lines.append(f"| {i} {s['Name']} | {number(c)} | {number(singleton)} | {number(jc)} | {number(jj)} | {min(jc,jj,singleton)/c:.2f}× | {number(isolated)} |")
    summary.append({'Scenario':i,'Name':s['Name'],'ClrMs':c,'JintClrMs':jc,'JintJsonMs':jj,'JintIsolatedMs':isolated,
                    'JintSingletonMs':singleton,'BestJintMode':best,'BestJintSpeedup':min(jc,jj,singleton)/c,'ClrBytesPerOp':value(i,'CRules','AllocatedBytes')/100000,
                    'BestJintBytesPerOp':value(i,best,'AllocatedBytes')/100000,
                    'ClrIncludingPrepareMs':statistics.median(r['ElapsedMs']+r['PrepareMs'] for r in pick(i,'CRules')),
                    'BestJintIncludingPrepareMs':min(statistics.median(r['ElapsedMs']+r['PrepareMs'] for r in pick(i,m)) for m in ['JintClr','JintJson','JintSingleton'])})
lines+=['','## 首次准备也计入时','',
        '主表的规则已经准备好。下表把每个进程首次规则准备（含首次加载和 JIT）加回该轮十万次计时，再取三轮中位数。HTML 可切换这两种计时口径。','',
        '| 场景 | CRules 准备加计算 ms | 最佳复用 Jint 准备加计算 ms | 耗时比 |',
        '| --- | ---: | ---: | ---: |']
for row in summary:
    lines.append(f"| {row['Scenario']} | {number(row['ClrIncludingPrepareMs'])} | {number(row['BestJintIncludingPrepareMs'])} | {row['BestJintIncludingPrepareMs']/row['ClrIncludingPrepareMs']:.2f}× |")
lines+=['','## 分配与内存','',
        '累计分配不是同时占用。GC 次数仅覆盖计时段；峰值工作集包含子进程启动、准备和预热。GC 后堆包含仍被句柄引用的规则、已 Dispose 的运行时对象和期望值数组，不是单独的引擎大小或泄漏证明。','',
        '| 场景与实现 | B/次 | 累计分配 MiB | Gen0 / Gen1 / Gen2 | 峰值工作集 MiB | GC 后堆 MiB |',
        '| --- | ---: | ---: | --- | ---: | ---: |']
for s in manifest['Scenarios']:
    for m in ['CRules','JintSingleton','JintClr','JintJson','JintIsolatedJson']:
        i=s['Id'];g='/'.join(str(int(value(i,m,k))) for k in ['Gen0','Gen1','Gen2'])
        lines.append(f"| {i} {m} | {number(value(i,m,'AllocatedBytes')/100000)} | {number(mib(value(i,m,'AllocatedBytes')))} | {g} | {number(mib(value(i,m,'PeakWorkingSet')))} | {number(mib(value(i,m,'ManagedHeapAfterCleanup')))} |")
lines+=['','## 数据如何构造','',
        '小 context 的前 16 份样本的 JSON 大小均值约 268 B，计时内分配约 384 B/次。大 context 的前 16 份样本的 JSON 大小均值约 11.69 KiB，计时内分配约 17304 B/次；包含 128 个指标、64 个八字段明细、16 个随机标签、32 个 metadata 字段和新分配的 4096 字符 payload。大对象的构建成本会摊薄引擎速度优势，不能将小对象微基准倍数推广到大对象。',
        '', '100 个局部变量是实际顺序依赖链，后一个读取前一个，结果消费 v99。复杂规则有 8 组 if/else-if/else 与嵌套 if，共 32 个条件节点；随机 flags 与 Vip 覆盖不同分支。完整规则与 context 示例在 rules 目录和 HTML 报告中。',
        '', '采用安全整数／定点计量计算，避免以 decimal 字符串 helper 的转换开销决定对比结论。本轮不声称任意 decimal 算法可无修改迁移到 JS Number。所有输出均远低于 2^53，主循环逐项对照独立循环参考实现生成的期望数组。',
        '', '固定随机种子 20261109；每次循环重新构建全部 context，不使用预生成对象或 JSON 字符串。期望结果数组在计时前生成。每个组合独立进程，预热 2000 次并强制 GC；原主测试三轮旋转实现顺序。SetValue 单例入口按补充要求另批运行三轮，种子、算法和引擎版本相同，详见 singleton-supplement.json。所有耗时为完整 Stopwatch 墙钟时间，没有微基准空调用开销扣除。',
        '', '部分 Jint 路径轮间波动较大，例如 S5 的 SetValue 单例三轮范围约 14.13–54.63 秒，中位数约 15.14 秒。HTML 保留每轮数据和范围；这些倍率是本机样本中位数，不是稳定延迟或性能保证。',
        '', '## 每次重新准备的补测','',
        '这一部分是独立的小样本准备成本测试：简单规则实际执行 1000 次，100 变量规则实际执行 200 次，每种组合 3 轮。CRules 与新 Engine 路径每次构建新执行器，包含新 context、测试规则文本生成、解析／编译和一次执行，不能视作纯解析器微基准。单例 Engine 路径提前生成规则文本，每次 SetValue 后 Evaluate 文本，逐次重新解析。没有外推成十万次。','',
        '| 场景 | 实际次数 | CRules 总 ms | 单例文本解析总 ms | 新 Engine CLR 总 ms | 新 Engine 隔离 JSON 总 ms |',
        '| --- | ---: | ---: | ---: | ---: | ---: |']
for s in manifest['Scenarios']:
    p=[r for r in preparation if r['Id']==s['Id']]
    v=lambda m:statistics.median(r['ElapsedMs'] for r in p if r['Mode']==m)
    lines.append(f"| {s['Id']} | {p[0]['Count']} | {number(v('CRules'))} | {number(v('JintSingletonParse'))} | {number(v('JintClr'))} | {number(v('JintIsolatedJson'))} |")
lines+=['','CRules 的准备包含类型绑定、审计、树组合和机器码编译，Jint 准备 AST。高频重复使用规则才能摊销这部分成本。是否每次重新准备会直接影响最终选择，应结合本表而非仅看热执行倍率。',
        '', '## 文件与复现','',
        '交互报告为 index.html，离线可打开。原始主记录为 results.json、workers 目录和 measurements.csv；准备成本为 preparation.json；manifest.json 保存环境、构造参数、分支覆盖计数和程序集哈希。',
        '', '```powershell',
        'dotnet build benchmarks/CRules.Benchmarks -c Release',
        'dotnet run --project benchmarks/CRules.Benchmarks -c Release --no-build -- --business-suite docs/performance/repeat-business 100000 3',
        'dotnet run --project benchmarks/CRules.Benchmarks -c Release --no-build -- --business-prepare docs/performance/repeat-business',
        'python benchmarks/reporting/build-business-report.py docs/performance/repeat-business',
        '```','',
        '本轮只改变测试工具，没有修改 CRules 核心执行器，也没有修改 TokenHub。主结论限于上述五种构造和精确整数／定点语义；不推广为所有业务规则的统一倍数。']
(folder/'分析报告.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
(folder/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
print('HTML bytes',len(html.encode()))
