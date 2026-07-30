namespace SkillRadar.Core.Models;

/// <summary>A diffed skill with a final ranking score attached by an IScorer.</summary>
public sealed record ScoredSkill
{
    public required DiffedSkill Diffed { get; init; }
    public required double Score { get; init; }

    /// <summary>Optional human-readable breakdown of how the score was derived (e.g. "star=4.2 fresh=3.0 density=0.5").</summary>
    public string? ScoreRationale { get; init; }

    public SkillRecord Skill => Diffed.Skill;
    public SkillChangeStatus Status => Diffed.Status;
}
