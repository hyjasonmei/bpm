namespace Bpm.Application.Purchase.Services;

/// Implements spec.decisions:
///   gateway_after_manager: amount >= 10000  → e5 (approval_finance), else → e4 (task_purchase_exec, default)
///   gateway_after_finance: amount >= 100000 → e8 (approval_ceo),     else → e7 (task_purchase_exec, default)
public static class PurchaseDecisionEvaluator
{
    public const decimal FinanceThreshold = 10000m;
    public const decimal CeoThreshold = 100000m;

    public const string EdgeAfterManagerToExec    = "e4";
    public const string EdgeAfterManagerToFinance = "e5";
    public const string EdgeAfterFinanceToExec    = "e7";
    public const string EdgeAfterFinanceToCeo     = "e8";

    public static string EvaluateAfterManager(decimal amount) =>
        amount >= FinanceThreshold ? EdgeAfterManagerToFinance : EdgeAfterManagerToExec;

    public static string EvaluateAfterFinance(decimal amount) =>
        amount >= CeoThreshold ? EdgeAfterFinanceToCeo : EdgeAfterFinanceToExec;
}
