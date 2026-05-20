using Cel.Checker;
using Cel.Tools;
using Cel.Common.Types.Json;
using Bpm.Admin.Application.Common.Abstractions;

namespace Bpm.Admin.Application.Spec.Expressions;

/// <summary>
/// CEL evaluator backed by Cel.NET 1.0.0. Ported from bpm-svc — the admin
/// service owns spec authoring, so the wizard's expression validator needs
/// to run here. All context variables are declared as <c>Decls.Dyn</c> so
/// the spec author writes <c>amount &gt; 50000</c> directly without
/// per-key type declarations. Custom functions registered via
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
