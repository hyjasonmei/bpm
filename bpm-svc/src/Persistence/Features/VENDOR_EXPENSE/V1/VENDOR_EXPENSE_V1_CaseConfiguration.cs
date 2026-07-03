using System.Text.Json;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.VENDOR_EXPENSE.V1;

public sealed class VENDOR_EXPENSE_V1_CaseConfiguration : IEntityTypeConfiguration<VENDOR_EXPENSE_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Configure(EntityTypeBuilder<VENDOR_EXPENSE_V1_Case> b)
    {
        b.ToTable("VENDOR_EXPENSE_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.Vendor).HasMaxLength(400);
        b.Property(c => c.SubmitterComment).HasMaxLength(2000);
        b.Property(c => c.SupervisorComment).HasMaxLength(2000);
        b.Property(c => c.ProcurementComment).HasMaxLength(2000);
        b.Property(c => c.SignComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        // Invoices repeater: TEXT column with JSON value conversion.
        // Per DB convention rule 6, never query into the JSON; treat as
        // an opaque blob from the DB's side.
        var invoicesConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<VENDOR_EXPENSE_V1_Invoice>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<VENDOR_EXPENSE_V1_Invoice>>(v, JsonOpts) ?? new());
        var invoicesComparer = new ValueComparer<List<VENDOR_EXPENSE_V1_Invoice>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<VENDOR_EXPENSE_V1_Invoice>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.Invoices)
            .HasColumnName("invoices_json")
            .HasConversion(invoicesConverter)
            .Metadata.SetValueComparer(invoicesComparer);

        b.Property(c => c.CurrentAssigneeRoleCode).HasMaxLength(60);
        b.HasIndex(c => new { c.CurrentAssigneeRoleCode, c.LastActivityAt });

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
