using Microsoft.OData.UriParser;

namespace Bpm.Admin.Api.Odata;

/// <summary>
/// Evaluates an OData $filter AST (parsed against the dynamic dataset EDM) against
/// a single row's cells, IN-MEMORY. AspNetCore.OData's [EnableQuery] can't filter
/// untyped EdmEntityObject collections, and CellsJson is TEXT (can't push to SQL
/// per the portability rules), so we walk the FilterClause ourselves. All dataset
/// columns are string in v1, so comparisons are string-based. Covers the operators
/// Power BI / Excel emit: eq / ne / and / or / gt-lt / contains / startswith /
/// endswith / tolower / toupper / trim.
/// </summary>
public static class DatasetFilterEvaluator
{
    /// <summary>Row = column key → cell value (null when absent). Id is included.</summary>
    public static bool Matches(FilterClause filter, IReadOnlyDictionary<string, string?> row)
        => ToBool(Eval(filter.Expression, row));

    private static object? Eval(QueryNode node, IReadOnlyDictionary<string, string?> row) => node switch
    {
        ConvertNode c => Eval(c.Source, row),
        ConstantNode k => k.Value?.ToString(),
        SingleValuePropertyAccessNode p => row.TryGetValue(p.Property.Name, out var v) ? v : null,
        SingleValueFunctionCallNode f => Func(f, row),
        BinaryOperatorNode b => Binary(b, row),
        UnaryOperatorNode u when u.OperatorKind == UnaryOperatorKind.Not => !ToBool(Eval(u.Operand, row)),
        _ => null,
    };

    private static object? Binary(BinaryOperatorNode b, IReadOnlyDictionary<string, string?> row)
    {
        // Logical operators short-circuit on the boolean operands.
        if (b.OperatorKind == BinaryOperatorKind.And) return ToBool(Eval(b.Left, row)) && ToBool(Eval(b.Right, row));
        if (b.OperatorKind == BinaryOperatorKind.Or) return ToBool(Eval(b.Left, row)) || ToBool(Eval(b.Right, row));

        var l = Eval(b.Left, row) as string;
        var r = Eval(b.Right, row) as string;
        var cmp = string.CompareOrdinal(l ?? string.Empty, r ?? string.Empty);
        return b.OperatorKind switch
        {
            BinaryOperatorKind.Equal => string.Equals(l, r, StringComparison.Ordinal),
            BinaryOperatorKind.NotEqual => !string.Equals(l, r, StringComparison.Ordinal),
            BinaryOperatorKind.GreaterThan => cmp > 0,
            BinaryOperatorKind.GreaterThanOrEqual => cmp >= 0,
            BinaryOperatorKind.LessThan => cmp < 0,
            BinaryOperatorKind.LessThanOrEqual => cmp <= 0,
            _ => false,
        };
    }

    private static object? Func(SingleValueFunctionCallNode f, IReadOnlyDictionary<string, string?> row)
    {
        var args = f.Parameters.Select(p => Eval(p, row) as string ?? string.Empty).ToArray();
        return f.Name.ToLowerInvariant() switch
        {
            "contains" when args.Length == 2 => args[0].Contains(args[1], StringComparison.Ordinal),
            "startswith" when args.Length == 2 => args[0].StartsWith(args[1], StringComparison.Ordinal),
            "endswith" when args.Length == 2 => args[0].EndsWith(args[1], StringComparison.Ordinal),
            "tolower" when args.Length == 1 => args[0].ToLowerInvariant(),
            "toupper" when args.Length == 1 => args[0].ToUpperInvariant(),
            "trim" when args.Length == 1 => args[0].Trim(),
            _ => null,
        };
    }

    private static bool ToBool(object? v) => v is bool b && b;
}
