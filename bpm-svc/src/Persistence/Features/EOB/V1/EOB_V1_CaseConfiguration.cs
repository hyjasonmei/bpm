using System.Text.Json;
using Bpm.Domain.Features.EOB.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.EOB.V1;

public sealed class EOB_V1_CaseConfiguration : IEntityTypeConfiguration<EOB_V1_Case>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Configure(EntityTypeBuilder<EOB_V1_Case> b)
    {
        b.ToTable("EOB_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        b.Property(c => c.LastName).IsRequired().HasMaxLength(100);
        b.Property(c => c.BusinessTitle).IsRequired().HasMaxLength(200);
        b.Property(c => c.EmployeeLocation).IsRequired().HasMaxLength(40);
        b.Property(c => c.RequireMailbox).HasMaxLength(10);
        b.Property(c => c.CostCenter).IsRequired().HasMaxLength(200);
        b.Property(c => c.ContractNumber).HasMaxLength(100);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        var conv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<EOB_V1_SetupTask>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<List<EOB_V1_SetupTask>>(v, JsonOpts) ?? new());
        var comp = new ValueComparer<List<EOB_V1_SetupTask>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<EOB_V1_SetupTask>>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());
        b.Property(c => c.SetupTasks).HasColumnName("setup_tasks_json").HasConversion(conv).Metadata.SetValueComparer(comp);

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
