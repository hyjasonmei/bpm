using Cel.Checker;
using Cel.Tools;
using Cel.Common.Types.Json;
using Bpm.Application.Common.Abstractions;

namespace Bpm.Application.Spec.Expressions;

/// <summary>
/// CEL evaluator backed by Cel.NET 1.0.0. All context variables are
/// declared as <c>Decls.Dyn</c> so the spec author writes
/// <c>amount &gt; 50000</c> directly without per-key type declarations.
/// CEL handles runtime type coercion. Custom functions registered via
/// <see cref="BpmCelLibrary"/>.
/// </summary>
public sealed class CelNetExpressionEvaluator(IClock clock) : IExpressionEvaluator
{
    private readonly ScriptHost _host = ScriptHost.NewBuilder()
        .Registry(JsonRegistry.NewRegistry())
        .Build();

    private readonly BpmCelLibrary _library = new(clock);

    public bool EvaluateBoolean(string expr, ExpressionContext ctx)
    {
        var (script, args) = BuildAndPrepare(expr, ctx);
        return script.Execute<bool>(args);
    }

    public object? EvaluateValue(string expr, ExpressionContext ctx)
    {
        var (script, args) = BuildAndPrepare(expr, ctx);
        return script.Execute<object>(args);
    }

    public ValidationResult Validate(string expr, ExpressionShape shape)
    {
        try
        {
            // Build the script with the union of all known declarations to
            // confirm parse + type-check succeeds. We pass an empty context
            // so any free variables show as type errors, which is what the
            // 9-stepper validator wants to surface.
            var builder = _host.BuildScript(expr).WithLibraries(_library);
            builder.Build();
            return ValidationResult.Ok();
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail(new ValidationError(ex.Message));
        }
    }

    private (Script, IDictionary<string, object>) BuildAndPrepare(string expr, ExpressionContext ctx)
    {
        var builder = _host.BuildScript(expr).WithLibraries(_library);

        // Declare every context key as Decls.Dyn so CEL accepts arbitrary
        // value types at runtime. This loses some compile-time checking
        // but is the right v1 trade-off — spec authors don't think in types.
        foreach (var key in ctx.FormData.Keys)
        {
            builder.WithDeclarations(Decls.NewVar(key, Decls.Dyn));
        }
        builder.WithDeclarations(Decls.NewVar("submitter", Decls.Dyn));
        if (ctx.Instance != null)
        {
            builder.WithDeclarations(Decls.NewVar("instance", Decls.Dyn));
        }

        var script = builder.Build();

        var args = new Dictionary<string, object>();
        foreach (var (k, v) in ctx.FormData)
        {
            if (v != null) args[k] = v;
        }
        args["submitter"] = ctx.Submitter;
        if (ctx.Instance != null) args["instance"] = ctx.Instance;

        return (script, args);
    }
}
