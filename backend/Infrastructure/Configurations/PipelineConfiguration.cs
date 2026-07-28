using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

internal sealed class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        builder.ToTable("pipelines");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);

        builder.HasMany(p => p.Stages)
            .WithOne()
            .HasForeignKey(s => s.PipelineId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Stages is a computed IReadOnlyList; EF Core must populate the backing field directly.
        builder.Navigation(p => p.Stages)
            .HasField("_stages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
