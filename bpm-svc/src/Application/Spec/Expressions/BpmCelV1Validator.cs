using System.Text.RegularExpressions;

namespace Bpm.Application.Spec.Expressions;

/// <summary>
/// Enforces the bpm-cel-v1 subset: rejects macros (filter / map / exists /
/// exists_one / all), bitwise operators, map literals, and slice syntax.
/// Uses the underlying evaluator's <c>Validate</c> first to confirm the
/// expression parses + type-checks at all, then runs lexical guards on
/// disallowed constructs.
///
/// v1 implementation is regex-based for simplicity. A proper AST walker
/// over Cel.NET's parsed Expr tree is the correct long-term shape — this
/// gets shipped now so the 9-stepper validator endpoint can exist.
/// </summary>
public sealed class BpmCelV1Validator(IExpressionEvaluator inner)
{
    private static readonly string[] BannedMacros =
    {
        "filter", "map", "exists", "exists_one", "all"
    };

    // Detects `x.macro(`, `x.macro (`, `].macro(` — i.e., macro applied to
    // an iterable. Bare identifiers named "filter" elsewhere (extremely
    // unlikely in spec context) are not flagged here; if needed, switch
    // to a parser-driven walker.
    private static readonly Regex MacroCall = new(
        @"\.\s*(filter|map|exists|exists_one|all)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex BitwiseOp = new(
        @"(?<![a-zA-Z0-9_])(&|\||\^|<<|>>)(?![a-zA-Z0-9_])",
        RegexOptions.Compiled);

    // Map literal: { "key": value }. We avoid matching {{ }} in mustache-ish
    // contexts since spec.json strings don't carry mustache; CEL never uses
    // a single-brace expression-level construct except for map literals.
    private static readonly Regex MapLiteral = new(
        @"\{\s*[""'][^""']*[""']\s*:",
        RegexOptions.Compiled);

    // Slice syntax: a[1:3]
    private static readonly Regex SliceSyntax = new(
        @"\[[^\]]*:[^\]]*\]",
        RegexOptions.Compiled);

    public ValidationResult Validate(string expression, ExpressionShape shape)
    {
        var errors = new List<ValidationError>();

        // First: parse + type-check via inner evaluator. Catches malformed
        // syntax up front so we don't run guards over garbage.
        var inner_result = inner.Validate(expression, shape);
        if (!inner_result.Valid)
        {
            return inner_result;
        }

        // Distinguish && and || from & and | (bitwise).
        // Strategy: scrub `&&` and `||` from the input before running the
        // bitwise guard.
        var scrubbed = expression
            .Replace("&&", "  ")
            .Replace("||", "  ");
        var bitwise = BitwiseOp.Matches(scrubbed);
        foreach (Match m in bitwise)
        {
            errors.Add(new ValidationError(
                $"operator '{m.Value}' not supported in bpm-cel-v1 (bitwise operators excluded)",
                Column: m.Index + 1));
        }

        var macros = MacroCall.Matches(expression);
        foreach (Match m in macros)
        {
            var name = m.Groups[1].Value;
            errors.Add(new ValidationError(
                $"macro '{name}' not supported in bpm-cel-v1 (defer to v1.5); use ActorRef DSL or restructure as plain comparison",
                Column: m.Index + 1));
        }

        var maps = MapLiteral.Matches(expression);
        foreach (Match m in maps)
        {
            errors.Add(new ValidationError(
                "map literal '{ ... }' not supported in bpm-cel-v1 (defer to v1.5)",
                Column: m.Index + 1));
        }

        var slices = SliceSyntax.Matches(expression);
        foreach (Match m in slices)
        {
            errors.Add(new ValidationError(
                $"slice syntax '{m.Value}' not supported in bpm-cel-v1 (defer to v1.5)",
                Column: m.Index + 1));
        }

        return errors.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(errors.ToArray());
    }
}
