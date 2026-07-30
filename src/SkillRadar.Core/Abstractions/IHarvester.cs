using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>
/// Reads SKILL.md frontmatter out of candidate repositories. Implementations must isolate
/// per-repository failures so one bad repo does not fail the whole batch.
/// </summary>
public interface IHarvester
{
    Task<IReadOnlyList<SkillRecord>> HarvestAsync(
        IReadOnlyList<RepoInfo> repositories,
        CancellationToken cancellationToken = default);
}
