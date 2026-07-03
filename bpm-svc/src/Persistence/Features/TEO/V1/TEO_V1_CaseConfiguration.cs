using System.Text.Json;
using Bpm.Domain.Features.TEO.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.TEO.V1;

public sealed class TEO_V1_CaseConfiguration : IEntityTypeConfiguration<TEO_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Configure(EntityTypeBuilder<TEO_V1_Case> b)
    {
        b.ToTable("TEO_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.TravelRequestNo).IsRequired().HasMaxLength(100);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.FinanceComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        // Expense items repeater: TEXT column with JSON value conversion.
        var conv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<TEO_V1_ExpenseItem>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<TEO_V1_ExpenseItem>>(v, JsonOpts) ?? new());
        var comp = new ValueComparer<List<TEO_V1_ExpenseItem>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<TEO_V1_ExpenseItem>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.ExpenseItems).HasColumnName("expense_items_json").HasConversion(conv).Metadata.SetValueComparer(comp);

        b.Property(c => c.CurrentAssigneeRoleCode).HasMaxLength(60);
        b.HasIndex(c => new { c.CurrentAssigneeRoleCode, c.LastActivityAt });

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
