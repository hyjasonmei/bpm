namespace Bpm.Application.Travel.Services;

/// Implements spec.decisions[gateway_intl]:
///   exclusive — destinationType == 'international' → e5 (approval_vp); else → e4 (task_admin_book, default)
public static class TravelDecisionEvaluator
{
    public const string EdgeToAdminBook = "e4";
    public const string EdgeToVp = "e5";

    public static string EvaluateIntlGateway(string destinationType) =>
        destinationType == "international" ? EdgeToVp : EdgeToAdminBook;
}
