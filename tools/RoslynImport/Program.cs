using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
Directory.SetCurrentDirectory(root);
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();
var cases = new List<object>();
var inventory = new List<object>();
int id = 0;
using var manifest = JsonDocument.Parse(File.ReadAllText(".tmp/roslyn-import-output/upstream.json"));
if (manifest.RootElement.GetProperty("commit").GetString() != "636a46f73c2006fad15339a5ba8c9ac5cd1c10c8") throw new InvalidOperationException("Unexpected upstream revision.");
foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray().OrderBy(e => e.GetProperty("localName").GetString(), StringComparer.Ordinal))
{
    string name = entry.GetProperty("localName").GetString()!;
    if (Path.GetFileName(name) != name) throw new InvalidOperationException("Invalid source file name.");
    var file = Path.Combine(".tmp/roslyn", name);
    var sourceText = File.ReadAllText(file).Replace("\r\n", "\n");
    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText)));
    if (hash != entry.GetProperty("sha256").GetString()) throw new InvalidOperationException("Upstream hash mismatch: " + name);
    var tree = CSharpSyntaxTree.ParseText(sourceText);
    foreach (var test in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.AttributeLists.ToString().Contains("Fact") || m.AttributeLists.ToString().Contains("Theory")))
    {
        int added = 0;
        HashSet<string> reasons = [];
        foreach (var literal in test.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            var source = literal.Token.ValueText;
            if (!source.Contains("class ") && !source.Contains("struct ")) continue;
            var payloadRoot = CSharpSyntaxTree.ParseText(source).GetRoot();
            if (Path.GetFileName(file) is "CodeGenImplicitlyTypeArraysTests.cs" or "ObjectAndCollectionInitializerTests.cs")
                foreach (var variable in payloadRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    var creation = variable.Initializer?.Value;
                    if (creation is not (ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax) && creation is not ObjectCreationExpressionSyntax { Type: GenericNameSyntax { Identifier.Text: "List" } }) continue;
                    var probe = CSharpCompilation.Create("Collection", [CSharpSyntaxTree.ParseText($"using System;using System.Collections.Generic;public static class C {{ public static object M() {{ return {creation}; }} }}")], refs, new(OutputKind.DynamicallyLinkedLibrary));
                    if (probe.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) { reasons.Add("集合创建表达式依赖原夹具或为负例"); continue; }
                    var probeTree = probe.SyntaxTrees.Single();
                    var value = probeTree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
                    var type = probe.GetSemanticModel(probeTree).GetTypeInfo(value).Type!;
                    bool UnsupportedArray(ITypeSymbol t) => t.IsAnonymousType || t is IArrayTypeSymbol array && (array.Rank != 1 || UnsupportedArray(array.ElementType));
                    if (UnsupportedArray(type)) { reasons.Add("匿名对象或多维数组初始化不在第一批范围"); continue; }
                    cases.Add(new
                    {
                        id = "R" + id++,
                        file = Path.GetFileName(file),
                        test = test.Identifier.Text,
                        method = "collection:" + variable.Identifier.Text,
                        line = tree.GetLineSpan(test.Span).StartLinePosition.Line + 1,
                        code = $"return {creation};",
                        returnType = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        parameters = Array.Empty<object>(),
                        adaptation = "提取独立集合创建表达式作为返回值；元素、类型、顺序保持原样，Console/LINQ 校验由差分断言替代"
                    });
                    added++;
                }
            foreach (var method in payloadRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var originalBody = method.Body ?? (method.ExpressionBody is null ? null : SyntaxFactory.Block(SyntaxFactory.ReturnStatement(method.ExpressionBody.Expression).NormalizeWhitespace()));
                if (originalBody is null || method.ReturnType.ToString() == "void") { reasons.Add("无独立非 void 函数体（输出或夹具在宿主中）"); continue; }
                if (!SupportedType(method.ReturnType.ToString()) || method.ParameterList.Parameters.Any(p => p.Type is null || !SupportedType(p.Type.ToString()) || p.Modifiers.Count != 0))
                { reasons.Add("签名依赖泛型、自定义类型、ref/out 或超出本轮的类型"); continue; }
                if (originalBody.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>().Any(n => n.Parent is not ReturnStatementSyntax || method.ReturnType.ToString() == "object"))
                { reasons.Add("嵌套目标类型 new() 或 object 目标类型不在本轮范围"); continue; }
                var rewrite = new PortableBody();
                var body = (BlockSyntax)rewrite.Visit(originalBody)!;
                if (rewrite.Reason is not null) { reasons.Add(rewrite.Reason); continue; }
                var mutableInputs = method.ParameterList.Parameters.Where(p => body.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a => a.Left is IdentifierNameSyntax name && name.Identifier.Text == p.Identifier.Text)).ToArray();
                var signature = method.ParameterList.ReplaceNodes(mutableInputs, (p, _) => p.WithIdentifier(SyntaxFactory.Identifier("input_" + p.Identifier.Text)));
                var code = string.Join("\n", mutableInputs.Select(p => $"var {p.Identifier} = input_{p.Identifier};").Concat(body.Statements.Select(s => s.ToString())));
                if (code.Contains("__") || method.ParameterList.Parameters.Any(p => p.Identifier.Text is "Math" or "Enumerable")) { reasons.Add("内部保留名"); continue; }
                var wrapper = $"using System; using System.Collections.Generic; public static class Oracle {{ public static {method.ReturnType} Evaluate{signature} {{ {code} }} }}";
                var compilation = CSharpCompilation.Create("Candidate", [CSharpSyntaxTree.ParseText(wrapper, new CSharpParseOptions(LanguageVersion.CSharp9))], refs, new(OutputKind.DynamicallyLinkedLibrary));
                var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0) { reasons.Add("函数依赖原夹具或原负例：" + string.Join(",", errors.Select(d => d.Id).Distinct())); continue; }
                var nativeTree = compilation.SyntaxTrees.Single();
                var nativeBody = nativeTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
                var semantic = compilation.GetSemanticModel(nativeTree);
                var switchExpressions = nativeBody.DescendantNodes().OfType<SwitchExpressionSyntax>().ToArray();
                if (switchExpressions.Any(s => s.Arms.LastOrDefault()?.Pattern is not DiscardPatternSyntax))
                { reasons.Add("switch 表达式缺少必须位于末尾的 _ 默认分支"); continue; }
                if (switchExpressions.Any(s => semantic.GetTypeInfo(s.GoverningExpression).Type?.SpecialType is SpecialType.System_Object or SpecialType.System_Double or SpecialType.System_Single))
                { reasons.Add("object 模式 switch 或 float/double 选择值不在 1.0 范围"); continue; }
                code = string.Join("\n", nativeBody.Statements.Where(s => semantic.AnalyzeControlFlow(s)!.StartPointIsReachable).Select(s => s.ToString()));
                var key = "R" + id++;
                cases.Add(new
                {
                    id = key,
                    file = Path.GetFileName(file),
                    test = test.Identifier.Text,
                    method = method.Identifier.Text,
                    line = tree.GetLineSpan(test.Span).StartLinePosition.Line + 1,
                    code,
                    returnType = method.ReturnType.ToString(),
                    parameters = signature.Parameters.Select(p => new { name = p.Identifier.Text, type = p.Type!.ToString() }).ToArray(),
                    adaptation = "保留函数核心；显式局部类型改为 var 加同类型转换；局部自增减改为 +=/-=1；可写形参复制到局部；移除 Console 输出、不可达尾句与 IL 断言"
                });
                added++;
            }
        }
        if (added == 0 && reasons.Count == 0) reasons.Add("源代码由拼接或辅助数据生成，或仅有语法树/诊断/IL 断言；不能直接提取独立函数体");
        inventory.Add(new { file = Path.GetFileName(file), test = test.Identifier.Text, line = tree.GetLineSpan(test.Span).StartLinePosition.Line + 1, imported = added, reasons = reasons.Order().ToArray() });
    }
}
Directory.CreateDirectory(".tmp/roslyn-import-output");
File.WriteAllText(".tmp/roslyn-import-output/cases.json", JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
File.WriteAllText(".tmp/roslyn-import-output/inventory.json", JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Inventoried {inventory.Count} upstream test methods, extracted {cases.Count} independently compilable non-void cores.");

static bool SupportedType(string type) => new[] { "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "char", "decimal", "float", "double", "string", "object" }.Any(t => type == t || type == t + "?" || type == t + "[]");

sealed class PortableBody : CSharpSyntaxRewriter
{
    public string? Reason;
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node is null || Reason is not null) return node;
        if (node is PrefixUnaryExpressionSyntax prefix && prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression || node is PostfixUnaryExpressionSyntax postfix && postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        { Reason = "表达式内自增减（本轮仅局部语句复合赋值）"; return node; }
        if (node is StatementSyntax && node is not (BlockSyntax or LocalDeclarationStatementSyntax or IfStatementSyntax or SwitchStatementSyntax or ReturnStatementSyntax or BreakStatementSyntax or ExpressionStatementSyntax))
        { Reason = "循环、异常、跳转、声明等非 Syntax 1.0 语句"; return node; }
        if (node is SwitchExpressionSyntax sw && sw.Arms.Any(a => a.WhenClause is not null || a.Pattern is not DiscardPatternSyntax && (a.Pattern is not ConstantPatternSyntax c || !LiteralLabel(c.Expression))))
        { Reason = "高级 switch 模式或 when"; return node; }
        if (node is PatternSyntax && node.Parent is not SwitchExpressionArmSyntax || node is AnonymousFunctionExpressionSyntax or AssignmentExpressionSyntax || node is ConditionalAccessExpressionSyntax or InterpolatedStringExpressionSyntax or CollectionExpressionSyntax or ThrowExpressionSyntax)
        {
            if (node is AssignmentExpressionSyntax a && a.Parent is ExpressionStatementSyntax && a.Left is IdentifierNameSyntax && a.Kind() is SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression) { }
            else { Reason = "模式、嵌套 switch、Lambda、嵌入赋值或空条件访问"; return node; }
        }
        if (node is CaseSwitchLabelSyntax label && !LiteralLabel(label.Value)) { Reason = "case 非字面/枚举常量（常量折叠不在 1.0 清单）"; return node; }
        return base.Visit(node);
    }
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax call && call.Expression.ToString() is "Console.Write" or "Console.WriteLine" or "System.Console.Write" or "System.Console.WriteLine") return null;
        if (node.Expression is PostfixUnaryExpressionSyntax p && p.Operand is IdentifierNameSyntax)
            return SyntaxFactory.ParseStatement($"{p.Operand} {(p.IsKind(SyntaxKind.PostIncrementExpression) ? "+=" : "-=")} 1;");
        if (node.Expression is not AssignmentExpressionSyntax) { Reason = "独立方法调用、自增或非局部赋值不在清单"; return node; }
        return base.VisitExpressionStatement(node);
    }
    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var declaration = node.Declaration;
        if (node.Modifiers.Any(SyntaxKind.ConstKeyword) || declaration.Variables.Count != 1 || declaration.Variables[0].Initializer is null)
        { Reason = "C# 编译期 const、多重声明或未初始化声明"; return node; }
        if (!declaration.Type.IsVar)
        {
            if (!new[] { "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "char", "decimal", "float", "double", "string", "object" }.Any(t => declaration.Type.ToString() == t || declaration.Type.ToString() == t + "?" || declaration.Type.ToString() == t + "[]"))
            { Reason = "自定义类型局部变量需要原夹具"; return node; }
            var v = declaration.Variables[0];
            node = SyntaxFactory.ParseStatement($"var {v.Identifier} = ({declaration.Type})({v.Initializer!.Value});") as LocalDeclarationStatementSyntax ?? node;
        }
        return base.VisitLocalDeclarationStatement(node);
    }
    static bool LiteralLabel(ExpressionSyntax node) => node is LiteralExpressionSyntax || node is MemberAccessExpressionSyntax || node is PrefixUnaryExpressionSyntax unary && unary.Operand is LiteralExpressionSyntax || node is CastExpressionSyntax cast && LiteralLabel(cast.Expression) || node is ParenthesizedExpressionSyntax paren && LiteralLabel(paren.Expression);
}
