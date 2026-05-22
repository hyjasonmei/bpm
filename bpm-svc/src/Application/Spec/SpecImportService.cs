using System.Text.Json;
using Bpm.Application.Spec.Expressions;

namespace Bpm.Application.Spec;

/// <summary>
/// Walks a posted spec.json and validates every CEL expression embedded in
/// gateways / form fields against the configured <see cref="IExpressionEvaluator"/>.
///
/// Locations covered (matches §4 of <c>add-cel-expressions/tasks.md</c>):
///   - <c>decisions[].branches[].condition</c>           — Boolean
///   - <c>userTasks[].fields[].conditional</c>           — Boolean
///   - <c>userTasks[].fields[].validator</c>             — Boolean
///   - <c>userTasks[].fields[].derivedFrom</c>           — Value
///
/// "Allowed fields" enforcement is best-effort. The wrapper's
/// <see cref="IExpressionEvaluator.Validate"/> doesn't take an allowed-set
/// parameter, so we drive scope-checking through the evaluation path: we
/// build a stub <see cref="ExpressionContext"/> containing every allowed
/// identifier (mapped to a permissive sentinel value) and then run the
/// expression. Cel.NET's own type-checker rejects references outside the
/// declared scope as "undeclared reference to 'x'" — that becomes the
/// "unknown identifier" signal the spec validator surfaces.
///
/// Limitations of v1:
///   - Member-access chains like <c>date_range.start</c> only validate the
///     root identifier; nested keys are accepted as long as the root is in
///     scope (the stub is a Dyn map, so any property lookup succeeds).
///   - Helpers like <c>businessDaysBetween</c> may surface a runtime error
///     when invoked against the placeholder values; we treat those as
///     <em>warnings only when the parse + type-check succeed</em> and a
///     real evaluation throws — which is currently swallowed because the
///     placeholder values aren't structurally valid for every helper.
///     A proper fix is an AST walker via Cel.NET's parsed Expr tree, but
///     extending the evaluator interface inflates this PR's scope.
/// </summary>
public interface ISpecImportService
{
    Task<SpecValidationResult> ValidateAsync(string specJson, CancellationToken ct = default);
}

public sealed record SpecValidationError(string Location, string Expression, string Message);

public sealed record SpecValidationResult(bool Valid, IReadOnlyList<SpecValidationError> Errors)
{
    public static SpecValidationResult Ok() => new(true, Array.Empty<SpecValidationError>());
    public static SpecValidationResult FromErrors(IReadOnlyList<SpecValidationError> errors)
        => new(errors.Count == 0, errors);
}

public sealed class SpecImportService(IExpressionEvaluator evaluator) : ISpecImportService
{
    public Task<SpecValidationResult> ValidateAsync(string specJson, CancellationToken ct = default)
    {
        var errors = new List<SpecValidationError>();

        if (string.IsNullOrWhiteSpace(specJson))
        {
            errors.Add(new SpecValidationError("$", "", "spec body is empty"));
            return Task.FromResult(SpecValidationResult.FromErrors(errors));
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(specJson); }
        catch (JsonException ex)
        {
            errors.Add(new SpecValidationError("$", "", $"invalid JSON: {ex.Message}"));
            return Task.FromResult(SpecValidationResult.FromErrors(errors));
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Build the global pool of top-level form keys (used by gateway
            // conditions). A single field id can show up in multiple
            // userTasks; that's fine — gateway scope is the union.
            var globalFormKeys = CollectGlobalFormKeys(root);

            ValidateGateways(root, globalFormKeys, errors);
            ValidateUserTaskFields(root, errors);
        }

        return Task.FromResult(SpecValidationResult.FromErrors(errors));
    }

    private static HashSet<string> CollectGlobalFormKeys(JsonElement root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("userTasks", out var userTasks) || userTasks.ValueKind != JsonValueKind.Array)
            return set;

        foreach (var ut in userTasks.EnumerateArray())
        {
            if (!ut.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var f in fields.EnumerateArray())
            {
                if (f.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var s = id.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) set.Add(s!);
                }
            }
        }
        return set;
    }

    private void ValidateGateways(JsonElement root, HashSet<string> allowed, List<SpecValidationError> errors)
    {
        if (!root.TryGetProperty("decisions", out var decisions) || decisions.ValueKind != JsonValueKind.Array)
            return;

        var idx = 0;
        foreach (var decision in decisions.EnumerateArray())
        {
            var gatewayId = decision.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? $"[{idx}]" : $"[{idx}]";

            if (decision.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array)
            {
                var bIdx = 0;
                foreach (var branch in branches.EnumerateArray())
                {
                    if (branch.TryGetProperty("condition", out var cond) && cond.ValueKind == JsonValueKind.String)
                    {
                        var expr = cond.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(expr))
                        {
                            ValidateOne(
                                location: $"gateway:{gatewayId}/branch[{bIdx}].condition",
                                expression: expr,
                                shape: ExpressionShape.Boolean,
                                allowed: allowed,
                                errors: errors);
                        }
                    }
                    bIdx++;
                }
            }
            idx++;
        }
    }

    private void ValidateUserTaskFields(JsonElement root, List<SpecValidationError> errors)
    {
        if (!root.TryGetProperty("userTasks", out var userTasks) || userTasks.ValueKind != JsonValueKind.Array)
            return;

        foreach (var ut in userTasks.EnumerateArray())
        {
            var taskId = ut.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? "?" : "?";

            if (!ut.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                continue;

            // Sibling field ids — used for conditional / validator / derivedFrom scope.
            var siblings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in fields.EnumerateArray())
            {
                if (f.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var s = id.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) siblings.Add(s!);
                }
            }

            foreach (var f in fields.EnumerateArray())
            {
                var fieldId = f.TryGetProperty("id", out var idEl2) && idEl2.ValueKind == JsonValueKind.String
                    ? idEl2.GetString() ?? "?" : "?";

                if (TryGetExpression(f, "conditional", out var condExpr))
                {
                    ValidateOne(
                        location: $"userTask:{taskId}/field:{fieldId}.conditional",
                        expression: condExpr!,
                        shape: ExpressionShape.Boolean,
                        allowed: siblings,
                        errors: errors);
                }

                if (TryGetExpression(f, "validator", out var valExpr))
                {
                    // Validator scope = siblings + `value` (the field's own value reference).
                    var scope = new HashSet<string>(siblings, StringComparer.Ordinal) { "value" };
                    ValidateOne(
                        location: $"userTask:{taskId}/field:{fieldId}.validator",
                        expression: valExpr!,
                        shape: ExpressionShape.Boolean,
                        allowed: scope,
                        errors: errors);
                }

                if (TryGetExpression(f, "derivedFrom", out var derExpr))
                {
                    ValidateOne(
                        location: $"userTask:{taskId}/field:{fieldId}.derivedFrom",
                        expression: derExpr!,
                        shape: ExpressionShape.Value,
                        allowed: siblings,
                        errors: errors);
                }
            }
        }
    }

    private static bool TryGetExpression(JsonElement field, string name, out string? expr)
    {
        if (field.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(el.GetString()))
        {
            expr = el.GetString();
            return true;
        }
        expr = null;
        return false;
    }

    private void ValidateOne(
        string location,
        string expression,
        ExpressionShape shape,
        HashSet<string> allowed,
        List<SpecValidationError> errors)
    {
        // Build a stub context: every allowed identifier is declared so
        // Cel.NET's type-checker doesn't reject the reference. The runtime
        // value is a Dyn-friendly sentinel (an empty dict) so member-access
        // chains like `date_range.start` don't NPE. References outside
        // <paramref name="allowed"/> surface as "undeclared reference to
        // 'x'" — that's the "unknown identifier" signal we want.
        var formData = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in allowed)
        {
            formData[name] = new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        var ctx = new ExpressionContext(
            FormData: formData,
            Submitter: new Dictionary<string, object?>(StringComparer.Ordinal));

        try
        {
            if (shape == ExpressionShape.Boolean)
                _ = evaluator.EvaluateBoolean(expression, ctx);
            else
                _ = evaluator.EvaluateValue(expression, ctx);
        }
        catch (Exception ex) when (IsParseOrScopeError(ex.Message))
        {
            errors.Add(new SpecValidationError(location, expression, ex.Message));
        }
        catch
        {
            // Runtime overload / coercion errors (e.g., `days >= 7` against a
            // stub dict yields "no such overload: map._>=_[](int)") are
            // expected — the placeholder data is intentionally untyped. Parse
            // + scope already passed, so the expression is structurally valid
            // for v1 purposes.
        }
    }

    private static bool IsParseOrScopeError(string message)
    {
        // Cel.NET wraps parser errors with "Syntax error" or
        // "ERROR: <input>:line:col: ..." text, and type-checker scope errors
        // with "undeclared reference to 'name'". Both are real spec authoring
        // bugs we want to surface. Anything else (e.g., overload resolution
        // against the placeholder runtime value) is swallowed.
        return message.Contains("Syntax error", StringComparison.Ordinal)
            || message.Contains("undeclared reference", StringComparison.Ordinal)
            || message.Contains("undefined field", StringComparison.Ordinal)
            || message.Contains("token recognition error", StringComparison.Ordinal);
    }
}
