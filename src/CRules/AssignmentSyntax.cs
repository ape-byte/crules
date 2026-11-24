using System.Text;
using DynamicExpresso.Exceptions;

namespace CRules;

// Finds only the boundaries needed to bind assignments. DynamicExpresso still
// parses the surrounding expression, including operators and overloads.
internal static class AssignmentSyntax
{
    internal readonly record struct Part(char Kind, int Start, int End);
    internal readonly record struct Replacement(int Start, int End, string Name);

    internal static bool MightAssign(string text) => text.Contains('=');

    internal static Part[] Parts(string text, int start, int end)
    {
        List<Part> parts = [];
        Stack<char> closes = new();
        int opening = 0;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            char before = i == start ? '\0' : text[i - 1];
            char after = i + 1 < end ? text[i + 1] : '\0';
            if (c is '"' or '\'' || c == '@' && after == '"')
            { i = StatementParser.SkipLiteral(text, i) - 1; continue; }
            if (c == '<' && after != '=' && GenericEnd(text, i, end) is int generic)
            { i = generic; continue; }
            if (c is '(' or '[' or '{')
            {
                if (closes.Count == 0) opening = i;
                closes.Push(c == '(' ? ')' : c == '[' ? ']' : '}');
                continue;
            }
            if (closes.Count > 0)
            {
                if (c == closes.Peek())
                {
                    closes.Pop();
                    if (closes.Count == 0) parts.Add(new(text[opening], opening, i + 1));
                }
                continue;
            }
            if (c is ',' or ':' || c == '?' && before != '?' && after is not ('?' or '.' or '['))
                parts.Add(new(c, i, i + 1));
            if (c == '=' && before is not ('=' or '!' or '<' or '>' or '&' or '|' or '^' or '?') && after is not ('=' or '>'))
            {
                if (before is '+' or '-' or '*' or '/' or '%') parts.Add(new(before, i - 1, i + 1));
                else parts.Add(new(c, i, i + 1));
            }
        }
        return parts.ToArray();
    }

    private static int? GenericEnd(string text, int open, int end)
    {
        int depth = 1;
        int first = open + 1;
        while (first < end && char.IsWhiteSpace(text[first])) first++;
        if (first == end || !(char.IsLetter(text[first]) || text[first] == '_')) return null;
        for (int i = first; i < end; i++)
        {
            char c = text[i];
            if (c == '<') depth++;
            else if (c == '>' && --depth == 0)
            {
                int next = i + 1;
                while (next < end && char.IsWhiteSpace(text[next])) next++;
                return next == end || text[next] is '(' or ')' or '.' or '[' or ']' or '{' or ',' or '?' ? i : null;
            }
            else if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c is not ('_' or '.' or ',' or '?' or '[' or ']' or '>')) return null;
        }
        return null;
    }

    internal static (string Text, IReadOnlyList<Replacement> Replacements) RewriteNested(
        string text, int maxDepth, Func<string, int, string> bind)
    {
        List<Replacement> replacements = [];
        Rewrite(0, text.Length, 0);
        if (replacements.Count == 0) return (text, replacements);
        var rewritten = new StringBuilder(text);
        for (int i = replacements.Count - 1; i >= 0; i--)
        {
            var (start, end, name) = replacements[i];
            rewritten.Remove(start, end - start).Insert(start, name);
        }
        return (rewritten.ToString(), replacements);

        void Rewrite(int start, int end, int depth)
        {
            if (text.IndexOf('=', start, end - start) < 0) return;
            if (depth > maxDepth)
                throw new ParseException("赋值表达式嵌套超过限额。", start);
            var parts = Parts(text, start, end);
            int valueStart = start;
            foreach (var comma in parts.Where(p => p.Kind == ','))
            {
                RewriteValue(valueStart, comma.Start);
                valueStart = comma.End;
            }
            RewriteValue(valueStart, end);

            void RewriteValue(int from, int to)
            {
                // The operands after ? and : are assignment expressions too.
                // Ignore punctuation inside groups when locating the matching colon.
                int questions = 0;
                Part question = default;
                foreach (var part in parts)
                {
                    if (part.Start < from || part.End > to) continue;
                    if (part.Kind == '=' && questions == 0)
                    {
                        while (from < to && char.IsWhiteSpace(text[from])) from++;
                        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
                        replacements.Add(new(from, to, bind(text[from..to], from)));
                        return;
                    }
                    if (part.Kind == '?')
                    {
                        if (questions == 0) question = part;
                        questions++;
                    }
                    if (part.Kind != ':' || questions == 0 || --questions != 0) continue;
                    Rewrite(from, question.Start, depth + 1);
                    Rewrite(question.End, part.Start, depth + 1);
                    Rewrite(part.End, to, depth + 1);
                    return;
                }
                foreach (var group in parts)
                    if (group.Start >= from && group.End <= to && group.Kind is '(' or '[')
                        Rewrite(group.Start + 1, group.End - 1, depth + 1);
            }
        }
    }

    internal static int OriginalPosition(int position, IReadOnlyList<Replacement> replacements)
    {
        int shift = 0;
        foreach (var (start, end, name) in replacements)
        {
            int rewrittenStart = start + shift;
            if (position < rewrittenStart) break;
            if (position < rewrittenStart + name.Length) return start;
            shift += name.Length - (end - start);
        }
        return position - shift;
    }
}
