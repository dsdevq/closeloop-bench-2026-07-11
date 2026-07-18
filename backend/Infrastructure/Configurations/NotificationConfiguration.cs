using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.RecipientUserId).IsRequired();
        builder.Property(n => n.Trigger).IsRequired();
        builder.Property(n => n.Title).IsRequired().HasMaxLength(500);
        builder.Property(n => n.Body).HasMaxLength(2000);
        builder.Property(n => n.IsRead).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });
    }
}
