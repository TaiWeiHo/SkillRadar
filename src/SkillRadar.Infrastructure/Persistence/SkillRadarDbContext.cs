using Microsoft.EntityFrameworkCore;

namespace SkillRadar.Infrastructure.Persistence;

public sealed class SkillRadarDbContext : DbContext
{
    public SkillRadarDbContext(DbContextOptions<SkillRadarDbContext> options) : base(options)
    {
    }

    public DbSet<SkillEntity> Skills => Set<SkillEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SkillEntity>(entity =>
        {
            entity.HasIndex(e => e.RepoFullName);
        });
    }
}
