using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SkillRadar.Infrastructure.Persistence;

/// <summary>EF Core entity backing the "Skills" table. Primary key is the natural key (repo + path).</summary>
[Table("Skills")]
public sealed class SkillEntity
{
    [Key]
    [MaxLength(512)]
    public string Key { get; set; } = default!;

    [MaxLength(255)]
    public string RepoFullName { get; set; } = default!;

    [MaxLength(255)]
    public string Path { get; set; } = default!;

    [MaxLength(255)]
    public string Name { get; set; } = default!;

    public string Description { get; set; } = default!;

    [MaxLength(64)]
    public string ContentHash { get; set; } = default!;

    public double LastScore { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }
}
