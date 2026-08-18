using Commitune.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Commitune.Infrastructure.Persistence;

public sealed class CommituneDbContext(DbContextOptions<CommituneDbContext> options) : DbContext(options)
{
    public DbSet<BotUser> Users => Set<BotUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommituneDbContext).Assembly);
}
