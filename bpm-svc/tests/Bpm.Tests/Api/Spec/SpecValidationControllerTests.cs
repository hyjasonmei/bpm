using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Bpm.Tests.Api.Spec;

public sealed class SpecValidationControllerTests
{
    private static SpecValidationController BuildController()
    {
        var evaluator = new CelNetExpressionEvaluator(new StubClock());
        var controller = new SpecValidationController(evaluator);
        var identity = new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test", "sub", "roles");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    [Fact]
    public void Valid_boolean_expression_returns_valid_true()
    {
        var controller = BuildController();
        var req = new ValidateExpressionRequest("days >= 7", "boolean", null);

        var result = controller.ValidateExpression(req);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ValidateExpressionResponse>(ok.Value);
        Assert.True(body.Valid);
        Assert.Null(body.Errors);
        Assert.Null(body.EvaluatedSample);
    }

    [Fact]
    public void Invalid_expression_returns_valid_false_with_errors()
    {
        var controller = BuildController();
        // `>=` with no rhs is a parse error.
        var req = new ValidateExpressionRequest("amount >=", "boolean", null);

        var result = controller.ValidateExpression(req);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ValidateExpressionResponse>(ok.Value);
        Assert.False(body.Valid);
        Assert.NotNull(body.Errors);
        Assert.NotEmpty(body.Errors!);
    }

    [Fact]
    public void Valid_with_sample_context_evaluates_boolean()
    {
        var controller = BuildController();
        var formData = JsonDocument.Parse("""{"days": 8}""").RootElement;
        var sample = new ValidateSampleContext(formData, Now: null);
        var req = new ValidateExpressionRequest("days >= 7", "boolean", sample);

        var result = controller.ValidateExpression(req);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ValidateExpressionResponse>(ok.Value);
        Assert.True(body.Valid);
        Assert.NotNull(body.EvaluatedSample);
        Assert.Equal(JsonValueKind.True, body.EvaluatedSample!.Value.ValueKind);
    }

    [Fact]
    public void Valid_with_sample_context_evaluates_value_shape()
    {
        var controller = BuildController();
        var formData = JsonDocument.Parse("""{"a": 3, "b": 4}""").RootElement;
        var sample = new ValidateSampleContext(formData, Now: null);
        var req = new ValidateExpressionRequest("a + b", "any", sample);

        var result = controller.ValidateExpression(req);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ValidateExpressionResponse>(ok.Value);
        Assert.True(body.Valid);
        Assert.NotNull(body.EvaluatedSample);
        Assert.Equal(7, body.EvaluatedSample!.Value.GetInt64());
    }

    [Fact]
    public void Empty_expression_returns_valid_false()
    {
        var controller = BuildController();
        var result = controller.ValidateExpression(new ValidateExpressionRequest("", "boolean", null));
        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ValidateExpressionResponse>(ok.Value);
        Assert.False(body.Valid);
        Assert.NotNull(body.Errors);
    }

    [Fact]
    public void Controller_class_is_authorize_protected()
    {
        // We don't spin up WebApplicationFactory; instead reflect to confirm
        // [Authorize] is present so CI catches an accidental removal.
        var attr = typeof(SpecValidationController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
    }
}
