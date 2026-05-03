using Bpm.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations;

public sealed class TravelCaseConfiguration : IEntityTypeConfiguration<TravelCase>
{
    public void Configure(EntityTypeBuilder<TravelCase> b)
    {
        b.ToTable("TravelCases");
        b.HasKey(c => c.Id);

        b.Property(c => c.TenantCode).IsRequired().HasMaxLength(64);
        b.Property(c => c.FlowCode).IsRequired().HasMaxLength(32);
        b.Property(c => c.State).HasConversion<int>().IsRequired();
        b.Property(c => c.ApplicantUserId).IsRequired().HasMaxLength(64);

        b.Property(c => c.DestinationType).IsRequired().HasMaxLength(32);
        b.Property(c => c.Destination).IsRequired().HasMaxLength(256);
        b.Property(c => c.DepartDate).IsRequired();
        b.Property(c => c.ReturnDate).IsRequired();
        b.Property(c => c.Purpose).IsRequired().HasMaxLength(2048);
        b.Property(c => c.EstimatedCost).HasPrecision(18, 2).IsRequired();

        b.Property(c => c.TicketRef).HasMaxLength(64);
        b.Property(c => c.HotelRef).HasMaxLength(256);
        b.Property(c => c.BookNote).HasMaxLength(2048);

        b.Property(c => c.CurrentApproverUserId).HasMaxLength(64);
        b.Property(c => c.ManagerApproverUserId).HasMaxLength(64);
        b.Property(c => c.VpApproverUserId).HasMaxLength(64);
        b.Property(c => c.AdminBookerUserId).HasMaxLength(64);
        b.Property(c => c.RejectedByUserId).HasMaxLength(64);
        b.Property(c => c.RejectionReason).HasMaxLength(1024);

        b.Property(c => c.CreatedAt).IsRequired();
        b.Property(c => c.UpdatedAt).IsRequired();
        b.Property(c => c.CreatedBy).IsRequired().HasMaxLength(64);

        b.Ignore(c => c.DomainEvents);

        b.HasIndex(c => new { c.TenantCode, c.State });
        b.HasIndex(c => c.ApplicantUserId);
        b.HasIndex(c => c.CurrentApproverUserId);
    }
}
