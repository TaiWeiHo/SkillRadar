using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>Tracks previously seen skills so the pipeline can tell new/updated/unchanged apart, and persists scores/history.</summary>
public interface IStateStore
{
    /// <summary>Compares a freshly harvested batch against known state. Does not write anything.</summary>
    Task<SkillDiffResult> DiffAsync(
        IReadOnlyList<SkillRecord> currentSkills,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the scored batch as the new known state (content hash, last seen, score history).</summary>
    Task PersistAsync(
        IReadOnlyList<ScoredSkill> scoredSkills,
        CancellationToken cancellationToken = default);
}
