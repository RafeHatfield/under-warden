namespace UnderWarden.Analyst;

/// <summary>Raised when a predicate cannot be parsed, or cannot be evaluated against a record.</summary>
public sealed class PredicateException(string message) : Exception(message);

/// <summary>
/// A parsed, reusable boolean predicate over a record's scalar fields. Supports the minimal
/// expression language the rubric predicates use:
///
///   expr        := orExpr
///   orExpr      := andExpr ('or' andExpr)*
///   andExpr     := notExpr ('and' notExpr)*
///   notExpr     := 'not' notExpr | comparison
///   comparison  := additive (('=='|'!='|'<'|'<='|'>'|'>=') additive)?
///   additive    := primary
///   primary     := number | 'true' | 'false' | identifier | '(' expr ')' | '-' primary
///
/// Identifiers resolve to <see cref="FieldValue"/> from the record's field map. Values are
/// numbers or bools; comparisons require numeric operands (== / != also allow bool==bool and
/// string==string). The top-level expression must evaluate to a bool.
///
/// Parsing happens once at rubric load (so a malformed predicate is a fatal load error, not a
/// silent never-fires). Evaluation is allocation-free per record.
/// </summary>
public sealed class PredicateExpression
{
    private readonly Node _root;

    /// <summary>The identifiers (field names) this predicate references — used for evidence + coverage.</summary>
    public IReadOnlyList<string> ReferencedFields { get; }

    public string Source { get; }

    private PredicateExpression(Node root, IReadOnlyList<string> fields, string source)
    {
        _root = root;
        ReferencedFields = fields;
        Source = source;
    }

    /// <summary>Parse a predicate string. Throws <see cref="PredicateException"/> on malformed input.</summary>
    public static PredicateExpression Parse(string text)
    {
        var tokens = Tokenize(text);
        var parser = new Parser(tokens, text);
        var root = parser.ParseExpr();
        parser.ExpectEnd();
        return new PredicateExpression(root, parser.Identifiers.ToList(), text);
    }

    /// <summary>Evaluate against a record's fields. Throws if a referenced field is missing.</summary>
    public bool Evaluate(IReadOnlyDictionary<string, FieldValue> fields)
    {
        var v = _root.Eval(fields);
        if (v.Type != FieldValue.Kind.Bool)
            throw new PredicateException($"predicate '{Source}' did not evaluate to a boolean.");
        return v.Bool;
    }

    // ── AST ──────────────────────────────────────────────────────────────────

    private abstract class Node { public abstract FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f); }

    private sealed class Lit(FieldValue value) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f) => value;
    }

    private sealed class Ident(string name) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f)
            => f.TryGetValue(name, out var v)
                ? v
                : throw new PredicateException($"predicate references unknown field '{name}'.");
    }

    private sealed class Neg(Node inner) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f)
        {
            var v = inner.Eval(f);
            if (v.Type != FieldValue.Kind.Number) throw new PredicateException("unary '-' requires a number.");
            return FieldValue.Number(-v.Num);
        }
    }

    private sealed class Not(Node inner) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f)
        {
            var v = inner.Eval(f);
            if (v.Type != FieldValue.Kind.Bool) throw new PredicateException("'not' requires a boolean operand.");
            return FieldValue.Boolean(!v.Bool);
        }
    }

    private sealed class BoolOp(bool isAnd, Node l, Node r) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f)
        {
            bool lb = AsBool(l.Eval(f), isAnd ? "and" : "or");
            // Short-circuit.
            if (isAnd && !lb) return FieldValue.Boolean(false);
            if (!isAnd && lb) return FieldValue.Boolean(true);
            return FieldValue.Boolean(AsBool(r.Eval(f), isAnd ? "and" : "or"));
        }

        private static bool AsBool(FieldValue v, string op) => v.Type == FieldValue.Kind.Bool
            ? v.Bool
            : throw new PredicateException($"'{op}' requires boolean operands.");
    }

    private sealed class Compare(string op, Node l, Node r) : Node
    {
        public override FieldValue Eval(IReadOnlyDictionary<string, FieldValue> f)
        {
            var a = l.Eval(f);
            var b = r.Eval(f);

            if (op is "==" or "!=")
            {
                bool eq = a.Type == b.Type && a.Type switch
                {
                    FieldValue.Kind.Number => a.Num.Equals(b.Num),
                    FieldValue.Kind.Bool   => a.Bool == b.Bool,
                    _                      => a.Str == b.Str,
                };
                return FieldValue.Boolean(op == "==" ? eq : !eq);
            }

            if (a.Type != FieldValue.Kind.Number || b.Type != FieldValue.Kind.Number)
                throw new PredicateException($"'{op}' requires numeric operands.");

            bool result = op switch
            {
                "<"  => a.Num < b.Num,
                "<=" => a.Num <= b.Num,
                ">"  => a.Num > b.Num,
                ">=" => a.Num >= b.Num,
                _    => throw new PredicateException($"unknown operator '{op}'."),
            };
            return FieldValue.Boolean(result);
        }
    }

    // ── Tokenizer ──────────────────────────────────────────────────────────────

    private enum T { Number, Ident, Op, LParen, RParen, And, Or, Not, True, False, End }
    private readonly record struct Token(T Type, string Text, double Num);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                var num = s[start..i];
                tokens.Add(new Token(T.Number, num, double.Parse(num, System.Globalization.CultureInfo.InvariantCulture)));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
                var word = s[start..i];
                tokens.Add(word switch
                {
                    "and"   => new Token(T.And, word, 0),
                    "or"    => new Token(T.Or, word, 0),
                    "not"   => new Token(T.Not, word, 0),
                    "true"  => new Token(T.True, word, 0),
                    "false" => new Token(T.False, word, 0),
                    _       => new Token(T.Ident, word, 0),
                });
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new Token(T.LParen, "(", 0)); i++; break;
                case ')': tokens.Add(new Token(T.RParen, ")", 0)); i++; break;
                case '=' or '!' or '<' or '>':
                {
                    bool twoChar = i + 1 < s.Length && s[i + 1] == '=';
                    if ((c == '=' || c == '!') && !twoChar)
                        throw new PredicateException($"malformed operator near '{s[i..]}' (use '==' / '!=').");
                    var op = twoChar ? s.Substring(i, 2) : s[i].ToString();
                    tokens.Add(new Token(T.Op, op, 0));
                    i += twoChar ? 2 : 1;
                    break;
                }
                case '-': tokens.Add(new Token(T.Op, "-", 0)); i++; break;
                default: throw new PredicateException($"unexpected character '{c}' in predicate.");
            }
        }
        tokens.Add(new Token(T.End, "", 0));
        return tokens;
    }

    // ── Parser ───────────────────────────────────────────────────────────────

    private sealed class Parser(List<PredicateExpression.Token> tokens, string source)
    {
        private int _pos;
        public HashSet<string> Identifiers { get; } = new(StringComparer.Ordinal);

        private Token Cur => tokens[_pos];
        private Token Advance() => tokens[_pos++];

        public void ExpectEnd()
        {
            if (Cur.Type != T.End)
                throw new PredicateException($"unexpected trailing tokens in predicate '{source}'.");
        }

        public Node ParseExpr() => ParseOr();

        private Node ParseOr()
        {
            var node = ParseAnd();
            while (Cur.Type == T.Or) { Advance(); node = new BoolOp(false, node, ParseAnd()); }
            return node;
        }

        private Node ParseAnd()
        {
            var node = ParseNot();
            while (Cur.Type == T.And) { Advance(); node = new BoolOp(true, node, ParseNot()); }
            return node;
        }

        private Node ParseNot()
        {
            if (Cur.Type == T.Not) { Advance(); return new Not(ParseNot()); }
            return ParseComparison();
        }

        private Node ParseComparison()
        {
            var left = ParsePrimary();
            if (Cur.Type == T.Op && Cur.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                var op = Advance().Text;
                return new Compare(op, left, ParsePrimary());
            }
            return left;
        }

        private Node ParsePrimary()
        {
            var tok = Cur;
            switch (tok.Type)
            {
                case T.Op when tok.Text == "-":
                    Advance();
                    return new Neg(ParsePrimary());
                case T.Number:
                    Advance();
                    return new Lit(FieldValue.Number(tok.Num));
                case T.True:
                    Advance();
                    return new Lit(FieldValue.Boolean(true));
                case T.False:
                    Advance();
                    return new Lit(FieldValue.Boolean(false));
                case T.Ident:
                    Advance();
                    Identifiers.Add(tok.Text);
                    return new Ident(tok.Text);
                case T.LParen:
                {
                    Advance();
                    var inner = ParseExpr();
                    if (Cur.Type != T.RParen)
                        throw new PredicateException($"missing ')' in predicate '{source}'.");
                    Advance();
                    return inner;
                }
                default:
                    throw new PredicateException($"unexpected token '{tok.Text}' in predicate '{source}'.");
            }
        }
    }
}
