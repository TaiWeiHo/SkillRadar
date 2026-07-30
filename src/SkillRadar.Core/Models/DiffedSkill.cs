namespace SkillRadar.Core.Models;

/// <summary>A harvested skill annotated with its change status relative to prior runs.</summary>
public sealed record DiffedSkill
{
    public required SkillRecord Skill { get; init; }
    public required SkillChangeStatus Status { get; init; }

    /// <summary>When this skill (by Key) was first seen across all runs. Equals today for new skills.</summary>
    public required DateTimeOffset FirstSeenUtc { get; init; }
}
