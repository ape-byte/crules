from pathlib import Path
import sys

rules = Path(sys.argv[1])
target = Path(sys.argv[2])
parts = ['// Generated from the same rule text; regenerate using generate-native-equivalent.py.',
         'using System.Runtime.CompilerServices;', 'namespace CRules.Benchmarks;',
         'public static class NativeBusinessEquivalent {',
         'public static Func<BusinessContext,long> Get(string id) => id switch { ' + ','.join(f'"S{i}" => S{i}' for i in range(1,6)) + ', _ => throw new ArgumentOutOfRangeException(nameof(id)) };']
for i in range(1, 6):
    code = (rules / f'S{i}.clr.txt').read_text(encoding='utf-8-sig')
    parts += ['[MethodImpl(MethodImplOptions.NoInlining)]', f'private static long S{i}(BusinessContext ctx) {{', code, '}']
parts.append('}')
target.write_text('\n'.join(parts) + '\n', encoding='utf-8', newline='\n')
