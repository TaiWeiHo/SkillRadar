namespace SkillRadar.Core.Models;

/// <summary>Result of comparing a freshly harvested batch of skills against known state.</summary>
public sealed record SkillDiffResult
{
    public required IReadOnlyList<DiffedSkill> Items { get; init; }

    public IEnumerable<DiffedSkill> NewSkills => Items.Where(i => i.Status == SkillChangeStatus.New);
    public IEnumerable<DiffedSkill> UpdatedSkills => Items.Where(i => i.Status == SkillChangeStatus.Updated);
    public IEnumerable<DiffedSkill> UnchangedSkills => Items.Where(i => i.Status == SkillChangeStatus.Unchanged);
}
