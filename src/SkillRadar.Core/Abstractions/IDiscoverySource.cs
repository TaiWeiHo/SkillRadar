using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>Finds candidate repositories that might contain Claude Skills.</summary>
public interface IDiscoverySource
{
    Task<IReadOnlyList<RepoInfo>> DiscoverRepositoriesAsync(CancellationToken cancellationToken = default);
}
