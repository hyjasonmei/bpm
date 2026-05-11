using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Tests.Common;
using Xunit;

namespace Bpm.Tests.Application.Spec;

public sealed class SpecImportServiceTests
{
    private static SpecImportService BuildSut()
    {
        var evaluator = new CelNetExpressionEvaluator(new StubClock());
        return new SpecImportService(evaluator);
    }

    [Fact]
    public async Task Valid_leave_v1_spec_passes()
    {
        var sut = BuildSut();
        var json = await File.ReadAllTextAsync("/Users/jason/claude/bpm/sample_specs/leave_v1.json");

        var result = await sut.ValidateAsync(json);

        Assert.True(result.Valid,
            "leave_v1.json should validate cleanly. Errors: " +
            string.Join(" | ", result.Errors.Select(e => $"{e.Location}: {e.Message}")));
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("expense_with_threshold_v1.json")]
    [InlineData("expense_employee_v1.json")]
    [InlineData("hardware_purchase_v1.json")]
    public async Task Sample_specs_with_cel_helpers_validate_cleanly(string fileName)
    {
        var sut = BuildSut();
        var json = await File.ReadAllTextAsync($"/Users/jason/claude/bpm/sample_specs/{fileName}");

        var result = await sut.ValidateAsync(json);

        Assert.True(result.Valid,
            $"{fileName} should validate cleanly. Errors: " +
            string.Join(" | ", result.Errors.Select(e => $"{e.Location}: {e.Message}")));
    }

    [Fact]
    public async Task Broken_gateway_condition_surfaces_location_and_parse_message()
    {
        var sut = BuildSut();
        // `days >= banana` parses fine syntactically (banana is a free
        // identifier) but is undeclared in the gateway's allowed scope —
        // top-level form keys: leave_type, date_range, days, reason, cert.
        var json = """
            {
              "meta": { "tenant": "acme", "flowCode": "LEAVE", "flowVersion": 1 },
              "userTasks": [
                { "id": "t1", "fields": [
                  { "id": "days", "type": "number" }
                ]}
              ],
              "decisions": [
                { "id": "gateway_days", "branches": [
                  { "edgeId": "e1", "condition": "days >= banana" }
                ]}
              ]
            }
            """;

        var result = await sut.ValidateAsync(json);

        Assert.False(result.Valid);
        var err = Assert.Single(result.Errors);
        Assert.Contains("gateway:gateway_days/branch[0].condition", err.Location);
        Assert.Equal("days >= banana", err.Expression);
        Assert.Contains("banana", err.Message);
    }

    [Fact]
    public async Task Field_conditional_referencing_unknown_sibling_fails()
    {
        var sut = BuildSut();
        // `cert.conditional` references `unknown_field` which is not a
        // sibling in task_apply.
        var json = """
            {
              "meta": { "tenant": "acme", "flowCode": "LEAVE", "flowVersion": 1 },
              "userTasks": [
                { "id": "task_apply", "fields": [
                  { "id": "leave_type", "type": "select" },
                  { "id": "cert", "type": "file", "conditional": "leave_type == '病假' || unknown_field == 'x'" }
                ]}
              ]
            }
            """;

        var result = await sut.ValidateAsync(json);

        Assert.False(result.Valid);
        var err = Assert.Single(result.Errors);
        Assert.Contains("userTask:task_apply/field:cert.conditional", err.Location);
        Assert.Contains("unknown_field", err.Message);
    }

    [Fact]
    public async Task Field_validator_can_reference_value_keyword()
    {
        var sut = BuildSut();
        // `value` is the field's own value reference and must be in scope
        // for validator expressions only.
        var json = """
            {
              "meta": { "tenant": "acme", "flowCode": "X", "flowVersion": 1 },
              "userTasks": [
                { "id": "t1", "fields": [
                  { "id": "amount", "type": "number", "validator": "value > 0" }
                ]}
              ]
            }
            """;

        var result = await sut.ValidateAsync(json);

        Assert.True(result.Valid,
            "validator scope must include `value`. Errors: " +
            string.Join(" | ", result.Errors.Select(e => $"{e.Location}: {e.Message}")));
    }

    [Fact]
    public async Task DerivedFrom_uses_helper_and_sibling_fields()
    {
        var sut = BuildSut();
        var json = """
            {
              "meta": { "tenant": "acme", "flowCode": "X", "flowVersion": 1 },
              "userTasks": [
                { "id": "t1", "fields": [
                  { "id": "date_range", "type": "daterange" },
                  { "id": "days", "type": "derived", "derivedFrom": "businessDaysBetween(date_range.start, date_range.end)" }
                ]}
              ]
            }
            """;

        var result = await sut.ValidateAsync(json);

        Assert.True(result.Valid,
            "derivedFrom that calls a registered helper on a sibling should pass. Errors: " +
            string.Join(" | ", result.Errors.Select(e => $"{e.Location}: {e.Message}")));
    }

    [Fact]
    public async Task Empty_body_is_invalid()
    {
        var sut = BuildSut();
        var result = await sut.ValidateAsync("");
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Invalid_json_is_invalid()
    {
        var sut = BuildSut();
        var result = await sut.ValidateAsync("{not json");
        Assert.False(result.Valid);
        Assert.Contains("invalid JSON", result.Errors[0].Message);
    }
}
