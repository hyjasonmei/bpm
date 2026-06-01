using System.Text.Json;
using Bpm.Domain.Features.ETM.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.ETM.V1;

public sealed class ETM_V1_CaseConfiguration : IEntityTypeConfiguration<ETM_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Configure(EntityTypeBuilder<ETM_V1_Case> b)
    {
        b.ToTable("ETM_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.EmployeeName).IsRequired().HasMaxLength(200);
        b.Property(c => c.EmployeeId).IsRequired().HasMaxLength(100);
        b.Property(c => c.Reason).IsRequired().HasMaxLength(40);
        b.Property(c => c.ProvideCertificate).HasMaxLength(10);
        b.Property(c => c.OutstandingPayment).HasMaxLength(10);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        var conv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<ETM_V1_ReturnItem>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<ETM_V1_ReturnItem>>(v, JsonOpts) ?? new());
        var comp = new ValueComparer<List<ETM_V1_ReturnItem>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ETM_V1_ReturnItem>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.ReturnItems).HasColumnName("return_items_json").HasConversion(conv).Metadata.SetValueComparer(comp);

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
