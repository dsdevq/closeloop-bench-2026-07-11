using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        builder.ToTable("deals");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Amount).IsRequired().HasPrecision(18, 4);

        builder.HasOne<Pipeline>()
            .WithMany()
            .HasForeignKey(d => d.PipelineId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PipelineStage>()
            .WithMany()
            .HasForeignKey(d => d.PipelineStageId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(d => d.CompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Contact>()
            .WithMany()
            .HasForeignKey(d => d.ContactId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
