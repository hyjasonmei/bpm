namespace Bpm.Application.Spec.Expressions;

public interface IExpressionEvaluator
{
    bool EvaluateBoolean(string expr, ExpressionContext ctx);

    object? EvaluateValue(string expr, ExpressionContext ctx);

    ValidationResult Validate(string expr, ExpressionShape shape);
}

public enum ExpressionShape
{
    Boolean,
    Value,
}

public sealed record ExpressionContext(
    IReadOnlyDictionary<string, object?> FormData,
    IReadOnlyDictionary<string, object?> Submitter,
    IReadOnlyDictionary<string, object?>? Instance = null);

public sealed record ValidationResult(bool Valid, IReadOnlyList<ValidationError> Errors)
{
    public static ValidationResult Ok() => new(true, Array.Empty<ValidationError>());
    public static ValidationResult Fail(params ValidationError[] errors) => new(false, errors);
}

public sealed record ValidationError(string Message, int Line = 0, int Column = 0);
