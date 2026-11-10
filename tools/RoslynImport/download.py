"""Download only the pinned official Roslyn test sources used by this audit."""
from hashlib import sha256
from pathlib import Path
import json
from urllib.request import urlopen

COMMIT = "636a46f73c2006fad15339a5ba8c9ac5cd1c10c8"
FILES = {
    **{name: "src/Compilers/CSharp/Test/Emit/CodeGen/" + name for name in (
        "SwitchTests.cs", "CodeGenTests.cs", "CodeGenOperators.cs",
        "ObjectAndCollectionInitializerTests.cs", "CodeGenImplicitlyTypeArraysTests.cs")},
    **{name: "src/Compilers/CSharp/Test/Emit3/Semantics/" + name for name in (
        "PatternMatchingTests2.cs", "PatternMatchingTests3.cs")},
    **{name: "src/Compilers/CSharp/Test/Emit3/FlowAnalysis/" + name for name in (
        "FlowTests.cs", "FlowDiagnosticTests.cs")},
    **{name: "src/Compilers/CSharp/Test/Semantic/Semantics/" + name for name in (
        "TargetTypedSwitchExpressionTests.cs", "ImplicitObjectCreationTests.cs")},
    "SemanticSwitchTests.cs": "src/Compilers/CSharp/Test/Semantic/Semantics/SwitchTests.cs",
}

def canonical(data):
    return data.decode("utf-8-sig").replace("\r\n", "\n").encode("utf-8")

if __name__ == "__main__":
    root = Path(__file__).resolve().parents[2]
    target = root / ".tmp/roslyn"
    target.mkdir(parents=True, exist_ok=True)
    manifest = {"repository": "https://github.com/dotnet/roslyn", "commit": COMMIT, "hashFormat": "SHA-256 of UTF-8 without BOM, LF", "files": []}
    for name, path in sorted(FILES.items()):
        url = f"https://raw.githubusercontent.com/dotnet/roslyn/{COMMIT}/{path}"
        with urlopen(url, timeout=60) as response:
            data = canonical(response.read())
        (target / name).write_bytes(data)
        manifest["files"].append({"localName": name, "path": path, "sha256": sha256(data).hexdigest(), "url": f"https://github.com/dotnet/roslyn/blob/{COMMIT}/{path}"})
    (root / ".tmp/roslyn-import-output").mkdir(exist_ok=True)
    (root / ".tmp/roslyn-import-output/upstream.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Downloaded {len(FILES)} pinned official test files to {target}.")
