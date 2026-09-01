using System.Globalization;
using System.Text;
using Sharp.Shell.Commands.Awk;
using Xunit;

namespace Sharp.Shell.Tests;

// The parser is asserted on the shape it produces, not on an answer an interpreter would give, so a
// precedence mistake is caught here rather than three phases later as a wrong number.
public class AwkParserTests
{
    [Theory]
    [InlineData("{ x = 1 + 2 * 3 }", "(= x (+ 1 (* 2 3)))")]
    [InlineData("{ x = (1 + 2) * 3 }", "(= x (* (group (+ 1 2)) 3))")]
    [InlineData("{ x = 2 ^ 3 ^ 2 }", "(= x (^ 2 (^ 3 2)))")]
    [InlineData("{ x = -2 ^ 2 }", "(= x (neg (^ 2 2)))")]
    [InlineData("{ x = 2 ^ -1 }", "(= x (^ 2 (neg 1)))")]
    [InlineData("{ x = !a ^ 2 }", "(= x (not (^ a 2)))")]
    [InlineData("{ x = a b c }", "(= x (concat (concat a b) c))")]
    [InlineData("{ x = a \" \" b }", "(= x (concat (concat a \" \") b))")]
    [InlineData("{ x = 1 - 1 }", "(= x (- 1 1))")]
    [InlineData("{ x = a \" \" -1 }", "(= x (concat a (- \" \" 1)))")]
    [InlineData("{ x = a == b }", "(= x (== a b))")]
    [InlineData("{ x = a b == c }", "(= x (== (concat a b) c))")]
    [InlineData("{ x = a ~ b == c }", "(= x (~ a (== b c)))")]
    [InlineData("{ x = a in b && c }", "(= x (&& (in (a) b) c))")]
    [InlineData("{ x = a && b || c }", "(= x (|| (&& a b) c))")]
    [InlineData("{ x = a ? b : c ? d : e }", "(= x (?: a b (?: c d e)))")]
    [InlineData("{ x = y = 1 }", "(= x (= y 1))")]
    [InlineData("{ x += 1 }", "(+= x 1)")]
    [InlineData("{ x = $1 }", "(= x (field 1))")]
    [InlineData("{ x = $NF - 1 }", "(= x (- (field NF) 1))")]
    [InlineData("{ $i++ }", "(post++ (field i))")]
    [InlineData("{ ++$i }", "(pre++ (field i))")]
    [InlineData("{ x = a[1, 2] }", "(= x (index a 1 2))")]
    [InlineData("{ x = (1, 2) in a }", "(= x (in (1 2) a))")]
    [InlineData("{ x = !a == b }", "(= x (== (not a) b))")]
    [InlineData("{ x = 6 / 2 }", "(= x (/ 6 2))")]
    public void PrecedenceFollowsPosix(string program, string expected)
    {
        Assert.Equal(expected, FirstStatementOf(program));
    }

    // POSIX makes the relational operators non-associative; awk reports `a < b < c` rather than
    // comparing c against a 0 or a 1.
    [Fact]
    public void RelationalOperatorsDoNotAssociate()
    {
        AwkParseResult result = AwkParser.Parse("{ x = a < b < c }");

        Assert.Null(result.Program);
        Assert.Null(result.UnsupportedConstruct);
        Assert.Contains("do not associate", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDanglingElseBindsToTheNearestIf()
    {
        Assert.Equal(
            "(if a (if b (print) (print)) )",
            FirstStatementOf("{ if (a) if (b) print; else print }"));
    }

    [Fact]
    public void AnElseOnItsOwnLineStillBindsToItsIf()
    {
        Assert.Equal("(if a (print) (print))", FirstStatementOf("{ if (a)\n print\nelse\n print\n}"));
    }

    [Fact]
    public void AForHeaderTellsMembershipFromTheThreePartForm()
    {
        Assert.Equal("(for-in k a (print))", FirstStatementOf("{ for (k in a) print }"));
        Assert.Equal("(for (= i 1) (< i 3) (post++ i) (print))", FirstStatementOf("{ for (i = 1; i < 3; i++) print }"));
    }

    [Fact]
    public void MembershipInsideAForHeaderIsStillAnOperator()
    {
        Assert.Equal(
            "(for (= i 0) (in (k) a) (post++ i) (print))",
            FirstStatementOf("{ for (i = 0; k in a; i++) print }"));
    }

    [Theory]
    [InlineData("{ x = /re/ }", "(= x /re/)")]
    [InlineData("{ x = $1 / 2 }", "(= x (/ (field 1) 2))")]
    [InlineData("{ x = a / 2 }", "(= x (/ a 2))")]
    [InlineData("{ x = a[1] / 2 }", "(= x (/ (index a 1) 2))")]
    [InlineData("{ x = (a) / 2 }", "(= x (/ (group a) 2))")]
    [InlineData("{ x = a++ / 2 }", "(= x (/ (post++ a) 2))")]
    [InlineData("{ split($0, parts, /,/) }", "(call split (field 0) parts /,/)")]
    [InlineData("{ sub(/a/, \"b\") }", "(call sub /a/ \"b\")")]
    public void ASlashIsARegexWhereAValueIsExpectedAndDivisionWhereOneEnded(string program, string expected)
    {
        Assert.Equal(expected, FirstStatementOf(program));
    }

    [Theory]
    [InlineData("{ print a > \"f\" }", "(print a > \"f\")")]
    [InlineData("{ print a >> \"f\" }", "(print a >> \"f\")")]
    [InlineData("{ print (a > b) }", "(print (> a b))")]
    [InlineData("{ print (a, b) }", "(print a b)")]
    [InlineData("{ print (a, b) > \"f\" }", "(print a b > \"f\")")]
    [InlineData("{ print (a) (b) }", "(print (concat (group a) (group b)))")]
    [InlineData("{ print }", "(print)")]
    [InlineData("{ print > \"f\" }", "(print > \"f\")")]
    [InlineData("{ printf \"%s\\n\", a }", "(printf \"%s\\n\" a)")]
    public void PrintTellsRedirectionFromComparison(string program, string expected)
    {
        Assert.Equal(expected, FirstStatementOf(program));
    }

    [Theory]
    [InlineData("{ while ((getline line) > 0) n++ }", "getline")]
    [InlineData("{ getline }", "getline")]
    [InlineData("{ getline x < \"f\" }", "getline")]
    [InlineData("{ \"date\" | getline d }", "|")]
    [InlineData("{ print | \"cat\" }", "|")]
    [InlineData("{ print |& \"cat\" }", "|&")]
    [InlineData("{ system(\"ls\") }", "system()")]
    [InlineData("{ x = gensub(/a/, \"b\", \"g\") }", "gensub()")]
    [InlineData("{ asort(a) }", "asort()")]
    [InlineData("{ asorti(a) }", "asorti()")]
    [InlineData("BEGIN { print systime() }", "systime()")]
    [InlineData("BEGIN { print strftime(\"%F\") }", "strftime()")]
    [InlineData("BEGIN { print mktime(\"2020 01 01 00 00 00\") }", "mktime()")]
    [InlineData("{ patsplit($0, a) }", "patsplit()")]
    [InlineData("BEGIN { IGNORECASE = 1 }", "IGNORECASE")]
    [InlineData("{ print RT }", "RT")]
    [InlineData("BEGIN { FIELDWIDTHS = \"1 2\" }", "FIELDWIDTHS")]
    [InlineData("BEGIN { FPAT = \"x\" }", "FPAT")]
    [InlineData("BEGIN { print PROCINFO[\"pid\"] }", "PROCINFO")]
    [InlineData("func f(a) { return a }", "func")]
    [InlineData("BEGIN { print 2 ** 3 }", "**")]
    [InlineData("BEGIN { x **= 2 }", "**=")]
    public void EveryRefusalNamesItsConstruct(string program, string construct)
    {
        AwkParseResult result = AwkParser.Parse(program);

        Assert.Null(result.Program);
        Assert.Null(result.Error);
        Assert.Equal(construct, result.UnsupportedConstruct);
    }

    // A refusal has to win over a syntax error, or a program with both would report an error and run
    // nothing instead of escalating to the native awk that can do it.
    [Fact]
    public void ARefusalOutranksASyntaxError()
    {
        AwkParseResult result = AwkParser.Parse("{ getline; x = = 1 }");

        Assert.Equal("getline", result.UnsupportedConstruct);
    }

    [Fact]
    public void AssigningToRsIsNotARefusal()
    {
        Assert.NotNull(AwkParser.Parse("BEGIN { RS = \"\" }").Program);
    }

    [Fact]
    public void NextfileAndFflushAreInTheDialect()
    {
        Assert.NotNull(AwkParser.Parse("{ nextfile }").Program);
        Assert.NotNull(AwkParser.Parse("{ fflush() }").Program);
    }

    [Fact]
    public void AFunctionDefinitionIsCollectedByName()
    {
        AwkProgram program = Parsed("function add(a, b) { return a + b } { print add(1, 2) }");

        Assert.True(program.Functions.ContainsKey("add"));
        Assert.Equal(["a", "b"], program.Functions["add"].Parameters);
        Assert.Single(program.Rules);
    }

    [Fact]
    public void APatternWithNoActionAndAnActionWithNoPatternBothParse()
    {
        AwkProgram program = Parsed("/x/\n{ print }\n");

        Assert.Equal(2, program.Rules.Count);
        Assert.NotNull(program.Rules[0].Pattern);
        Assert.Null(program.Rules[0].Action);
        Assert.Null(program.Rules[1].Pattern);
        Assert.NotNull(program.Rules[1].Action);
    }

    [Fact]
    public void ARangePatternKeepsBothEnds()
    {
        AwkProgram program = Parsed("/a/, /b/ { print }");

        Assert.IsType<AwkRangePattern>(program.Rules[0].Pattern);
    }

    [Fact]
    public void ACommentAndALineContinuationAreNotTokens()
    {
        Assert.Equal("(= x (+ 1 2))", FirstStatementOf("{ # a comment\n x = 1 + \\\n 2 }"));
    }

    [Fact]
    public void AStringKeepsItsEscapes()
    {
        Assert.Equal(
            "(print \"a\\tb\\nc/dAq\")",
            FirstStatementOf("{ print \"a\\tb\\nc\\/d\\101\\q\" }"));
    }

    [Fact]
    public void ANewlineAfterAnOperatorContinuesTheExpression()
    {
        Assert.Equal("(= x (&& a b))", FirstStatementOf("{ x = a &&\n b }"));
    }

    [Fact]
    public void AnUnterminatedStringIsAnErrorRatherThanARefusal()
    {
        AwkParseResult result = AwkParser.Parse("BEGIN { print \"oops }");

        Assert.Null(result.UnsupportedConstruct);
        Assert.Contains("unterminated string", result.Error!, StringComparison.Ordinal);
    }

    // Every program in the agent corpus has to reach the interpreter, or the applet would escalate on
    // exactly the lines it exists to own.
    [Theory]
    [MemberData(nameof(AgentOneLiners))]
    public void EveryAgentOneLinerParses(string program)
    {
        AwkParseResult result = AwkParser.Parse(program);

        Assert.Null(result.UnsupportedConstruct);
        Assert.Null(result.Error);
    }

    public static TheoryData<string> AgentOneLiners =>
    [
        "{print $2}",
        "{print $1}",
        "NR>1 && NR<=20",
        "{s+=$1} END {print s}",
        "!seen[$0]++",
        "{printf \"%-20s %s\\n\", $1, $2}",
        "/pattern/ {print FILENAME\":\"FNR\": \"$0}",
        "BEGIN{FS=\",\"} {print NF}",
        "END{print NR}",
        "$3 > 100 {c++} END{print c+0}",
        "{gsub(/[ \\t]+$/, \"\"); print}",
        "length($0) > 80",
        "{ for (i = NF; i > 0; i--) printf \"%s%s\", $i, (i > 1 ? OFS : ORS) }",
        "{ t[$1] += $2 } END { for (k in t) print k, t[k] }",
        "{ if ($1 ~ /^#/) next; print }",
        "BEGIN { while ((n = n + 1) < 3) print n }",
        "function max(a, b) { return a > b ? a : b } { print max($1, $2) }",
        "{ print substr($0, 1, 10) }",
        "{ n = split($0, parts, \":\"); print n, parts[1] }",
        "{ print > \"out.txt\" }",
        "{ print $0 >> \"log.txt\" }",
        "{ sub(/^ */, \"\"); print }",
        "BEGIN { OFS = \"\\t\" } { $1 = $1; print }",
        "{ a[NR] = $0 } END { for (i = NR; i >= 1; i--) print a[i] }",
        "{ printf \"%5.2f\\n\", $1 / 3 }",
        "$0 ~ /x/ && $0 !~ /y/",
        "{ delete seen[$1] }",
        "{ do { i++ } while (i < 3); print i }",
        "{ next } END { print NR }",
        "{ nextfile }",
        "BEGIN { exit 3 }",
        "{ print toupper($1), tolower($2) }",
        "BEGIN { printf \"%c%c\\n\", 65, \"BC\" }",
        "{ if (match($0, /[0-9]+/)) print RSTART, RLENGTH }",
        "BEGIN { SUBSEP = \":\"; a[1, 2] = 3; for (k in a) print k }",
        "BEGIN { print ENVIRON[\"HOME\"] }",
        "BEGIN { srand(1); print rand() < 1 }",
        "{ print $NF }",
        "{ $2 = \"\"; print }",
        "{ NF = 2; print }",
    ];

    private static AwkProgram Parsed(string source)
    {
        AwkParseResult result = AwkParser.Parse(source);
        Assert.Null(result.UnsupportedConstruct);
        Assert.Null(result.Error);
        return result.Program!;
    }

    private static string FirstStatementOf(string source)
    {
        AwkProgram program = Parsed(source);
        return AwkSyntaxRenderer.Render(program.Rules[0].Action!.Statements[0]);
    }
}

// A parenthesised rendering of the tree, so a precedence assertion reads as the shape it means
// rather than as a chain of type checks.
internal static class AwkSyntaxRenderer
{
    public static string Render(AwkStatement statement) => statement switch
    {
        AwkBlock block => $"(block {Join(block.Statements.Select(Render))})",
        AwkExpressionStatement expression => Render(expression.Expression),
        AwkIfStatement branch => $"(if {Render(branch.Condition)} {Render(branch.Then)} {Optional(branch.Else)})",
        AwkWhileStatement loop => $"(while {Render(loop.Condition)} {Render(loop.Body)})",
        AwkDoWhileStatement loop => $"(do {Render(loop.Body)} {Render(loop.Condition)})",
        AwkForStatement loop => RenderFor(loop),
        AwkForInStatement loop => $"(for-in {loop.VariableName} {loop.ArrayName} {Render(loop.Body)})",
        AwkBreakStatement => "(break)",
        AwkContinueStatement => "(continue)",
        AwkNextStatement => "(next)",
        AwkNextFileStatement => "(nextfile)",
        AwkExitStatement exit => $"(exit{Suffix(exit.Status)})",
        AwkReturnStatement value => $"(return{Suffix(value.Value)})",
        AwkDeleteStatement delete => $"(delete {delete.ArrayName}{Suffix(delete.Subscripts)})",
        AwkPrintStatement print => $"(print{Suffix(print.Arguments)}{Suffix(print.Redirection)})",
        AwkPrintfStatement print => $"(printf{Suffix(print.Arguments)}{Suffix(print.Redirection)})",
        AwkEmptyStatement => "(empty)",
        _ => "(?)",
    };

    private static string RenderFor(AwkForStatement loop) =>
        $"(for {Optional(loop.Initialiser)} {Optional(loop.Condition)} {Optional(loop.Update)} {Render(loop.Body)})";

    public static string Render(AwkExpression expression) => expression switch
    {
        AwkNumberLiteral number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        AwkStringLiteral text => $"\"{Escaped(text.Value)}\"",
        AwkRegexLiteral pattern => $"/{pattern.Pattern}/",
        AwkVariable variable => variable.Name,
        AwkField field => $"(field {Render(field.Index)})",
        AwkArrayElement element => $"(index {element.Name}{Suffix(element.Subscripts)})",
        AwkGrouping group => $"(group {Render(group.Inner)})",
        AwkAssignment assignment => $"({Spelling(assignment.Operator)} {Render(assignment.Target)} {Render(assignment.Value)})",
        AwkTernary ternary => $"(?: {Render(ternary.Condition)} {Render(ternary.WhenTrue)} {Render(ternary.WhenFalse)})",
        AwkBinary binary => $"({Spelling(binary.Operator)} {Render(binary.Left)} {Render(binary.Right)})",
        AwkUnary unary => $"({Spelling(unary.Operator)} {Render(unary.Operand)})",
        AwkIncrement increment => $"({(increment.IsPrefix ? "pre" : "post")}{(increment.IsDecrement ? "--" : "++")} {Render(increment.Target)})",
        AwkMatchExpression match => $"({(match.IsNegated ? "!~" : "~")} {Render(match.Subject)} {Render(match.Pattern)})",
        AwkMembership membership => $"(in ({Join(membership.Subscripts.Select(Render))}) {membership.ArrayName})",
        AwkSubscriptList list => $"(subscripts {Join(list.Subscripts.Select(Render))})",
        AwkCall call => $"(call {call.Name}{Suffix(call.Arguments)})",
        AwkBuiltinCall call => $"(call {call.Builtin.ToString().ToLowerInvariant()}{Suffix(call.Arguments)})",
        _ => "(?)",
    };

    private static string Optional(AwkStatement? statement) => statement is null ? string.Empty : Render(statement);

    private static string Optional(AwkExpression? expression) => expression is null ? string.Empty : Render(expression);

    private static string Suffix(AwkExpression? expression) => expression is null ? string.Empty : $" {Render(expression)}";

    private static string Suffix(IReadOnlyList<AwkExpression> expressions) =>
        expressions.Count == 0 ? string.Empty : $" {Join(expressions.Select(Render))}";

    private static string Suffix(AwkRedirection? redirection) => redirection is null
        ? string.Empty
        : $" {(redirection.Kind == AwkRedirectionKind.Truncate ? ">" : ">>")} {Render(redirection.Target)}";

    private static string Join(IEnumerable<string> parts) => string.Join(' ', parts);

    // A decoded string is rendered with its escapes back on, so an assertion can be written as a
    // one-line C# literal instead of embedding a real tab.
    private static string Escaped(string text)
    {
        StringBuilder escaped = new();

        foreach (char character in text)
        {
            escaped.Append(character switch
            {
                '\n' => "\\n",
                '\t' => "\\t",
                '\r' => "\\r",
                '\\' => "\\\\",
                '"' => "\\\"",
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }

    private static string Spelling(AwkAssignOperator assignment) => assignment switch
    {
        AwkAssignOperator.Assign => "=",
        AwkAssignOperator.Add => "+=",
        AwkAssignOperator.Subtract => "-=",
        AwkAssignOperator.Multiply => "*=",
        AwkAssignOperator.Divide => "/=",
        AwkAssignOperator.Modulo => "%=",
        _ => "^=",
    };

    private static string Spelling(AwkBinaryOperator binary) => binary switch
    {
        AwkBinaryOperator.Or => "||",
        AwkBinaryOperator.And => "&&",
        AwkBinaryOperator.Less => "<",
        AwkBinaryOperator.LessOrEqual => "<=",
        AwkBinaryOperator.Greater => ">",
        AwkBinaryOperator.GreaterOrEqual => ">=",
        AwkBinaryOperator.Equal => "==",
        AwkBinaryOperator.NotEqual => "!=",
        AwkBinaryOperator.Concatenate => "concat",
        AwkBinaryOperator.Add => "+",
        AwkBinaryOperator.Subtract => "-",
        AwkBinaryOperator.Multiply => "*",
        AwkBinaryOperator.Divide => "/",
        AwkBinaryOperator.Modulo => "%",
        _ => "^",
    };

    private static string Spelling(AwkUnaryOperator unary) => unary switch
    {
        AwkUnaryOperator.Negate => "neg",
        AwkUnaryOperator.Plus => "pos",
        _ => "not",
    };
}
