using Bpm.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations;

public sealed class PurchaseCaseConfiguration : IEntityTypeConfiguration<PurchaseCase>
{
    public void Configure(EntityTypeBuilder<PurchaseCase> b)
    {
        b.ToTable("PurchaseCases");
        b.HasKey(c => c.Id);

        b.Property(c => c.TenantCode).IsRequired().HasMaxLength(64);
        b.Property(c => c.FlowCode).IsRequired().HasMaxLength(32);
        b.Property(c => c.State).HasConversion<int>().IsRequired();
        b.Property(c => c.ApplicantUserId).IsRequired().HasMaxLength(64);

        b.Property(c => c.Vendor).IsRequired().HasMaxLength(256);
        b.Property(c => c.Category).IsRequired().HasMaxLength(32);
        b.Property(c => c.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(c => c.Items).IsRequired().HasMaxLength(4096);
        b.Property(c => c.Justification).IsRequired().HasMaxLength(2048);
        b.Property(c => c.QuoteFileName).HasMaxLength(256);

        b.Property(c => c.PoNumber).HasMaxLength(64);
        b.Property(c => c.ExpectedDelivery);
        b.Property(c => c.ExecNote).HasMaxLength(2048);

        b.Property(c => c.CurrentApproverUserId).HasMaxLength(64);
        b.Property(c => c.ManagerApproverUserId).HasMaxLength(64);
        b.Property(c => c.FinanceApproverUserId).HasMaxLength(64);
        b.Property(c => c.CeoApproverUserId).HasMaxLength(64);
        b.Property(c => c.PurchaseExecUserId).HasMaxLength(64);
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
