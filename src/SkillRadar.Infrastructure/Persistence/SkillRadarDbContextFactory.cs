using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SkillRadar.Infrastructure.Persistence;

/// <summary>Design-time factory so `dotnet ef migrations add` can construct the context without running the full host.</summary>
public sealed class SkillRadarDbContextFactory : IDesignTimeDbContextFactory<SkillRadarDbContext>
{
    public SkillRadarDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SkillRadarDbContext>();
        builder.UseSqlite("Data Source=state/skills.db");
        return new SkillRadarDbContext(builder.Options);
    }
}
