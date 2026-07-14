using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Type).IsRequired();
        builder.Property(a => a.Note).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.OccurredAt).IsRequired();

        // Restrict (not SetNull): nulling the sole anchor would violate the domain's
        // exactly-one-anchor invariant enforced in Activity.Create.
        builder.HasOne<Contact>()
            .WithMany()
            .HasForeignKey(a => a.ContactId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Deal>()
            .WithMany()
            .HasForeignKey(a => a.DealId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
