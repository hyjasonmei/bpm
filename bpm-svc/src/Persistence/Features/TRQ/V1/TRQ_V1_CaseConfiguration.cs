using Bpm.Domain.Features.TRQ.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.TRQ.V1;

public sealed class TRQ_V1_CaseConfiguration : IEntityTypeConfiguration<TRQ_V1_Case>
{
    public void Configure(EntityTypeBuilder<TRQ_V1_Case> b)
    {
        b.ToTable("TRQ_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.TravelType).IsRequired().HasMaxLength(40);
        b.Property(c => c.DepartureCity).IsRequired().HasMaxLength(200);
        b.Property(c => c.DestinationCity).IsRequired().HasMaxLength(200);
        b.Property(c => c.ChargeTo).IsRequired().HasMaxLength(200);
        b.Property(c => c.TravelPurpose).IsRequired().HasMaxLength(2000);
        b.Property(c => c.PassportName).HasMaxLength(200);
        b.Property(c => c.SeatPreference).HasMaxLength(40);
        b.Property(c => c.PickupRequired).HasMaxLength(10);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
