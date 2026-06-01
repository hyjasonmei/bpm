using System.Text.Json;
using Bpm.Domain.Features.FAP.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.FAP.V1;

public sealed class FAP_V1_CaseConfiguration : IEntityTypeConfiguration<FAP_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Configure(EntityTypeBuilder<FAP_V1_Case> b)
    {
        b.ToTable("FAP_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.ShippingLocation).IsRequired().HasMaxLength(200);
        b.Property(c => c.ChargeTo).IsRequired().HasMaxLength(200);
        b.Property(c => c.Purpose).IsRequired().HasMaxLength(40);
        b.Property(c => c.Note).HasMaxLength(2000);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.PurchaseOrderNo).HasMaxLength(40);
        b.Property(c => c.Received).HasMaxLength(20);
        b.Property(c => c.VerificationRemark).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        var conv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<FAP_V1_PurchaseItem>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<FAP_V1_PurchaseItem>>(v, JsonOpts) ?? new());
        var comp = new ValueComparer<List<FAP_V1_PurchaseItem>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<FAP_V1_PurchaseItem>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.PurchaseItems).HasColumnName("purchase_items_json").HasConversion(conv).Metadata.SetValueComparer(comp);

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
