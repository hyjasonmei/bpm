using Bpm.Application.Purchase.Services;
using FluentAssertions;

namespace Bpm.Tests.Unit;

public class PurchaseDecisionEvaluatorTests
{
    [Theory]
    [InlineData(0,        "e4")]
    [InlineData(9999.99,  "e4")]
    [InlineData(10000,    "e5")]
    [InlineData(10000.01, "e5")]
    [InlineData(99999.99, "e5")]
    [InlineData(100000,   "e5")]
    public void After_manager_threshold_is_inclusive_at_10000(decimal amount, string expected)
    {
        // spec.decisions[gateway_after_manager]: amount >= 10000 → e5 (approval_finance), default e4 (exec)
        PurchaseDecisionEvaluator.EvaluateAfterManager(amount).Should().Be(expected);
    }

    [Theory]
    [InlineData(0,         "e7")]
    [InlineData(99999.99,  "e7")]
    [InlineData(100000,    "e8")]
    [InlineData(100000.01, "e8")]
    [InlineData(1_000_000, "e8")]
    public void After_finance_threshold_is_inclusive_at_100000(decimal amount, string expected)
    {
        // spec.decisions[gateway_after_finance]: amount >= 100000 → e8 (approval_ceo), default e7 (exec)
        PurchaseDecisionEvaluator.EvaluateAfterFinance(amount).Should().Be(expected);
    }
}
