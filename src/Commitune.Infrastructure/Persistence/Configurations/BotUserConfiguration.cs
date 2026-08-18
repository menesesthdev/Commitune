using Commitune.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Commitune.Infrastructure.Persistence.Configurations;

public sealed class BotUserConfiguration : IEntityTypeConfiguration<BotUser>
{
    public void Configure(EntityTypeBuilder<BotUser> builder)
    {
        builder.ToTable("bot_users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        builder.HasIndex(u => u.TelegramUserId).IsUnique();

        builder.Property(u => u.State)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(u => u.GithubLogin).HasMaxLength(100);

        // Data Protection ciphertext is base64url and grows with the payload; no length cap
        // so a longer token format can't silently truncate.
        builder.Property(u => u.ProtectedGithubToken);

        builder.Property(u => u.RepositoryOwner).HasMaxLength(100);
        builder.Property(u => u.RepositoryName).HasMaxLength(100);

        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();
    }
}
