using Bpm.Application.Travel.Services;
using FluentAssertions;

namespace Bpm.Tests.Unit;

public class TravelDecisionEvaluatorTests
{
    [Theory]
    [InlineData("domestic",      "e4")]
    [InlineData("international", "e5")]
    [InlineData("",              "e4")]  // unknown → default
    public void Routes_to_VP_only_for_international(string destinationType, string expected)
    {
        TravelDecisionEvaluator.EvaluateIntlGateway(destinationType).Should().Be(expected);
    }
}
