using System.Text.Json;
using System.Text.RegularExpressions;
using Bpm.Application.Spec.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Spec;

/// <summary>
/// Live expression-validation endpoint for the 9-step authoring wizard.
/// Front-end debounces 500 ms and posts the in-progress expression so the
/// editor can show inline ✓ / ✗ status.
///
/// Returns 200 even on validation failures — the body's <c>valid</c> flag is
/// the success signal, not the HTTP status. That keeps the front-end happy
/// path simple (no try/catch around expected user typos) and stays consistent
/// with the spec's "errors are data" framing.
/// </summary>
[ApiController]
[Route("api/specs")]
[Authorize]
public sealed class SpecValidationController(IExpressionEvaluator evaluator) : ControllerBase
{
    // Identifier-ish tokens, used to pre-declare free variables before the
    // CEL type-checker rejects them. Excludes leading-dot members so
    // `foo.bar` only declares `foo`.
    private static readonly Regex IdentifierRegex = new(
        @"(?<![\w\.])([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    // CEL keywords + bpm-cel helpers we never want to declare as a free var.
    // Keep in sync with BpmCelLibrary.cs.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "in",
        "size", "string", "int", "double", "bool", "bytes", "duration", "timestamp",
        "matches", "startsWith", "endsWith", "type",
        // bpm helpers
        "businessDaysBetween", "sum", "count", "avg", "min", "max",
        "dateAdd", "dateDiff", "match", "length", "contains", "now",
        // ambient
        "submitter", "instance",
    };

    [HttpPost("validate-expression")]
    public IActionResult ValidateExpression([FromBody] ValidateExpressionRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Expression))
        {
            return Ok(new ValidateExpressionResponse(
                Valid: false,
                Errors: new[] { "expression is required" }));
        }

        var shape = string.Equals(req.Shape, "boolean", StringComparison.OrdinalIgnoreCase)
            ? ExpressionShape.Boolean
            : ExpressionShape.Value;

        // When a sample context is provided we run a *real* eval and
        // distinguish parse-time vs runtime failures by which step throws.
        // Otherwise we fall back to a stub context that pre-declares every
        // identifier-like token so the evaluator's type-checker doesn't
        // reject free variables — this gives the wizard a "is this CEL
        // even valid?" check independent of any sample data. Runtime overload
        // failures against the stub data are expected (the dict sentinel
        // can't be compared to int) and are swallowed; only true parse /
        // scope errors surface.
        if (req.SampleContext is null)
        {
            var stubCtx = BuildStubContext(req.Expression);
            try
            {
                if (shape == ExpressionShape.Boolean)
                    _ = evaluator.EvaluateBoolean(req.Expression, stubCtx);
                else
                    _ = evaluator.EvaluateValue(req.Expression, stubCtx);
            }
            catch (Exception ex) when (IsParseOrScopeError(ex.Message))
            {
                return Ok(new ValidateExpressionResponse(
                    Valid: false,
                    Errors: new[] { ex.Message }));
            }
            catch
            {
                // Stub-runtime overload failure — parse + scope already passed.
            }
            return Ok(new ValidateExpressionResponse(Valid: true));
        }

        // Sample context provided — eval against it. Runtime exceptions are
        // returned as { valid: true, errors: [<runtime>], evaluatedSample: null }
        // when parse looked OK but sample data crashed the helper. We can't
        // cheaply tell parse vs runtime apart with the current wrapper, so
        // we err on the side of "valid: true" + surface the message.
        try
        {
            var ctx = BuildContext(req.SampleContext);
            JsonElement evaluated;
            if (shape == ExpressionShape.Boolean)
            {
                var b = evaluator.EvaluateBoolean(req.Expression, ctx);
                evaluated = JsonSerializer.SerializeToElement(b);
            }
            else
            {
                var v = evaluator.EvaluateValue(req.Expression, ctx);
                evaluated = JsonSerializer.SerializeToElement(v);
            }
            return Ok(new ValidateExpressionResponse(
                Valid: true,
                EvaluatedSample: evaluated));
        }
        catch (Exception ex)
        {
            return Ok(new ValidateExpressionResponse(
                Valid: true,
                Errors: new[] { $"runtime error during sample evaluation: {ex.Message}" }));
        }
    }

    private static ExpressionContext BuildStubContext(string expression)
    {
        var formData = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (Match m in IdentifierRegex.Matches(expression))
        {
            var name = m.Groups[1].Value;
            if (Reserved.Contains(name)) continue;
            // Permissive Dyn map so both `name` and `name.anything` work.
            formData[name] = new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        return new ExpressionContext(
            FormData: formData,
            Submitter: new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    private static bool IsParseOrScopeError(string message)
        => message.Contains("Syntax error", StringComparison.Ordinal)
        || message.Contains("undeclared reference", StringComparison.Ordinal)
        || message.Contains("undefined field", StringComparison.Ordinal)
        || message.Contains("token recognition error", StringComparison.Ordinal);

    private static ExpressionContext BuildContext(ValidateSampleContext sample)
    {
        var formData = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (sample.FormData is JsonElement fd && fd.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in fd.EnumerateObject())
            {
                formData[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        // submitter must be a non-null map for ExpressionContext; the wizard
        // doesn't carry submitter info during authoring so an empty placeholder
        // is the right default.
        var submitter = new Dictionary<string, object?>(StringComparer.Ordinal);

        return new ExpressionContext(formData, submitter);
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value), StringComparer.Ordinal),
        _ => el.ToString(),
    };
}

public sealed record ValidateExpressionRequest(string Expression, string Shape, ValidateSampleContext? SampleContext);

public sealed record ValidateSampleContext(JsonElement? FormData, string? Now);

public sealed record ValidateExpressionResponse(
    bool Valid,
    IReadOnlyList<string>? Errors = null,
    JsonElement? EvaluatedSample = null);
