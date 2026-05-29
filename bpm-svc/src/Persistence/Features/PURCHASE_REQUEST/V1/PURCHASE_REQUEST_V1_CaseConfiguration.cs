using System.Text.Json;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.PURCHASE_REQUEST.V1;

public sealed class PURCHASE_REQUEST_V1_CaseConfiguration : IEntityTypeConfiguration<PURCHASE_REQUEST_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Configure(EntityTypeBuilder<PURCHASE_REQUEST_V1_Case> b)
    {
        b.ToTable("PURCHASE_REQUEST_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.SubmitterComment).IsRequired().HasMaxLength(2000);
        b.Property(c => c.DeptHeadComment).HasMaxLength(2000);
        b.Property(c => c.FinanceComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        // Invoices repeater: TEXT column with JSON value conversion.
        // Per DB convention rule 6, never query into the JSON; treat as
        // an opaque blob from the DB's side.
        var invoicesConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<PURCHASE_REQUEST_V1_Invoice>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<PURCHASE_REQUEST_V1_Invoice>>(v, JsonOpts) ?? new());
        var invoicesComparer = new ValueComparer<List<PURCHASE_REQUEST_V1_Invoice>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<PURCHASE_REQUEST_V1_Invoice>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.Invoices)
            .HasColumnName("invoices_json")
            .HasConversion(invoicesConverter)
            .Metadata.SetValueComparer(invoicesComparer);

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
