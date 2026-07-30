using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>Ranks diffed skills. v1 uses pure heat (stars/freshness/density); v2 can add LLM relevance scoring.</summary>
public interface IScorer
{
    Task<IReadOnlyList<ScoredSkill>> ScoreAsync(
        SkillDiffResult diff,
        CancellationToken cancellationToken = default);
}
