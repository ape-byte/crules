namespace CRules;

internal readonly record struct Slice(int Start, int Length);
internal abstract record Statement(SourceSpan Span);
internal sealed record Block(List<Statement> Statements, SourceSpan Location) : Statement(Location);
internal sealed record Declaration(string Name, bool ReadOnly, Slice Value, SourceSpan Location) : Statement(Location);
internal sealed record Assignment(string Name, Slice Value, SourceSpan Location) : Statement(Location);
internal sealed record Conditional(Slice Condition, Block Then, Statement? Else, SourceSpan Location) : Statement(Location);
internal sealed record Return(Slice Value, SourceSpan Location) : Statement(Location);
internal sealed class CompileFailure(string code, string message, SourceSpan span,
    PositionPrecision precision = PositionPrecision.Point) : Exception(message)
{
    public string Code { get; } = code;
    public SourceSpan Span { get; } = span;
    public PositionPrecision Precision { get; } = precision;
}

/// <summary>只识别语句与表达式边界；表达式语法与类型完全交给 DynamicExpresso。</summary>
internal sealed class StatementParser(string source, RuleCompileOptions options)
{
    private int _pos;
    private int _count;
    private int _depth;
    public string Text { get; } = MaskComments(source);

    public Block Parse() => ReadBlock(false);

    private Block ReadBlock(bool braced)
    {
        if (++_depth > options.MaxDepth) Fail("CRL4001", "代码块嵌套超过限额。", _pos);
        Skip();
        int start = _pos;
        if (braced) Expect('{');
        List<Statement> statements = [];
        while (true)
        {
            Skip();
            if (_pos == Text.Length)
            {
                if (braced) Fail("CRL1001", "代码块缺少右花括号。", start);
                break;
            }
            if (Text[_pos] == '}')
            {
                if (!braced) Fail("CRL1001", "意外的右花括号。", _pos);
                _pos++; break;
            }
            statements.Add(ReadStatement());
        }
        _depth--;
        return new(statements, new(start, _pos - start));
    }

    private Statement ReadStatement()
    {
        if (++_count > options.MaxStatements) Fail("CRL4001", "语句数量超过限额。", _pos);
        Skip(); int start = _pos;
        if (Text[_pos] == '{') return ReadBlock(true);
        string keyword = Identifier();
        if (keyword == "if")
        {
            Expect('('); var condition = ExpressionUntil(')');
            var then = ReadBlock(true);
            Statement? other = null;
            Skip();
            if (AtWord("else"))
            {
                _pos += 4; Skip();
                other = AtWord("if") ? ReadStatement() : ReadBlock(true);
            }
            return new Conditional(condition, then, other, new(start, _pos - start));
        }
        if (keyword == "return")
            return new Return(ExpressionUntil(';'), new(start, _pos - start));
        if (keyword is "var" or "const")
        {
            var name = Identifier(); Expect('=');
            var expr = ExpressionUntil(';');
            return new Declaration(name, keyword == "const", expr, new(start, _pos - start));
        }
        if (keyword is "for" or "foreach" or "while" or "do" or "try" or "catch" or "finally"
            or "throw" or "async" or "await" or "using" or "switch" or "goto" or "yield")
            Fail("CRL2001", $"不支持语句 {keyword}。", start);
        Expect('=');
        return new Assignment(keyword, ExpressionUntil(';'), new(start, _pos - start));
    }

    private Slice ExpressionUntil(char terminator)
    {
        Skip(); int start = _pos;
        Stack<char> stack = new();
        while (_pos < Text.Length)
        {
            char c = Text[_pos];
            if (c == terminator && stack.Count == 0)
            {
                int end = _pos++;
                while (end > start && char.IsWhiteSpace(Text[end - 1])) end--;
                if (end == start) Fail("CRL1002", "表达式不能为空。", start);
                if (end - start > options.MaxExpressionLength) Fail("CRL4001", "表达式长度超过限额。", start);
                return new(start, end - start);
            }
            if (c == '"' || c == '\'' || (c == '@' && Peek(_pos + 1) == '"'))
            { _pos = SkipLiteral(Text, _pos); continue; }
            if (c is '$' or '#') Fail("CRL2001", "不支持插值字符串或预处理指令。", _pos);
            if (c is '(' or '[' or '{')
            {
                stack.Push(c);
                if (stack.Count > options.MaxDepth) Fail("CRL4001", "表达式嵌套超过限额。", _pos);
            }
            else if (c is ')' or ']' or '}')
            {
                if (stack.Count == 0 || !Matches(stack.Pop(), c)) Fail("CRL1001", "括号不匹配或缺少分号。", _pos);
            }
            _pos++;
        }
        Fail("CRL1001", $"表达式缺少结束符 {terminator}。", start);
        return default;
    }

    private string Identifier()
    {
        Skip(); int start = _pos;
        if (_pos >= Text.Length || !(char.IsLetter(Text[_pos]) || Text[_pos] == '_'))
            Fail("CRL1002", "此处需要标识符。", _pos);
        while (_pos < Text.Length && (char.IsLetterOrDigit(Text[_pos]) || Text[_pos] == '_')) _pos++;
        return Text[start.._pos];
    }
    private bool AtWord(string value) => Text.AsSpan(_pos).StartsWith(value, StringComparison.Ordinal)
        && (_pos + value.Length == Text.Length || !(char.IsLetterOrDigit(Text[_pos + value.Length]) || Text[_pos + value.Length] == '_'));
    private void Skip() { while (_pos < Text.Length && char.IsWhiteSpace(Text[_pos])) _pos++; }
    private void Expect(char value)
    {
        Skip();
        if (_pos >= Text.Length || Text[_pos] != value) Fail("CRL1001", $"此处需要 {value}。", _pos);
        _pos++;
    }
    private char Peek(int p) => p < Text.Length ? Text[p] : '\0';
    private static bool Matches(char a, char b) => (a, b) is ('(', ')') or ('[', ']') or ('{', '}');
    private static void Fail(string code, string message, int pos) => throw new CompileFailure(code, message, new(pos, 1));

    internal static string MaskComments(string text)
    {
        char[] chars = text.ToCharArray();
        for (int i = 0; i < text.Length;)
        {
            if (text[i] is '"' or '\'' || text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            { i = SkipLiteral(text, i); continue; }
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                int start = i;
                bool block = text[i + 1] == '*'; i += 2;
                if (block)
                {
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    if (i + 1 == text.Length || i >= text.Length) Fail("CRL1001", "块注释未闭合。", start);
                    i += 2;
                }
                else while (i < text.Length && text[i] is not ('\r' or '\n')) i++;
                for (int j = start; j < i; j++) if (chars[j] is not ('\r' or '\n')) chars[j] = ' ';
                continue;
            }
            i++;
        }
        return new(chars);
    }

    internal static int SkipLiteral(string text, int start)
    {
        bool verbatim = text[start] == '@';
        int pos = start + (verbatim ? 2 : 1);
        char quote = verbatim ? '"' : text[start];
        if (!verbatim && quote == '"' && pos + 1 < text.Length && text[pos] == '"' && text[pos + 1] == '"')
            Fail("CRL2001", "首版不支持原始字符串。", start);
        while (pos < text.Length)
        {
            if (!verbatim && text[pos] == '\\') { pos += 2; continue; }
            if (text[pos] == quote)
            {
                if (verbatim && pos + 1 < text.Length && text[pos + 1] == quote) { pos += 2; continue; }
                return pos + 1;
            }
            if (!verbatim && text[pos] is '\r' or '\n') Fail("CRL1001", "普通字面量不能跨行。", start);
            pos++;
        }
        Fail("CRL1001", "字符串或字符字面量未闭合。", start);
        return 0;
    }
}
