namespace Bpm.Domain.Features.TEO.V1;

/// <summary>
/// One row inside the Travel Expense line-item repeater. Persisted as a
/// JSON array on the case row (single TEXT column) per DB convention
/// rule 6 (keep JSON queries out of the DB layer).
/// </summary>
public sealed record TEO_V1_ExpenseItem(
    DateOnly Date,
    string?  Country,
    string?  Category,
    string?  Description,
    string?  Amount,
    string?  AmountLcy);
