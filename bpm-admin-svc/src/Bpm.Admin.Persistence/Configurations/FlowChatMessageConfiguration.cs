using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class FlowChatMessageConfiguration : IEntityTypeConfiguration<FlowChatMessage>
{
    public void Configure(EntityTypeBuilder<FlowChatMessage> builder)
    {
        builder.ToTable("FlowChatMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sender).HasConversion<int>();
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(40);

        builder.HasIndex(x => x.FlowId);
        builder.HasIndex(x => new { x.FlowId, x.CreatedAt });
    }
}
