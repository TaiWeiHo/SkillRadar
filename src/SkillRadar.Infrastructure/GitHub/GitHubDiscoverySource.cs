using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.GitHub;

/// <summary>
/// Finds candidate repos via GitHub Search Repositories (by topic), plus a configured seed list.
/// We deliberately avoid GitHub Code Search here — its index is incomplete/eventually-consistent
/// and it has its own tighter, less predictable rate limits; Search Repositories + Git Trees (used
/// by the harvester) is the documented-stable combination for this use case.
/// </summary>
public sealed class GitHubDiscoverySource : IDiscoverySource
{
    private readonly GitHubApiClient _client;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubDiscoverySource> _logger;

    public GitHubDiscoverySource(GitHubApiClient client, IOptions<GitHubOptions> options, ILogger<GitHubDiscoverySource> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RepoInfo>> DiscoverRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var byFullName = new Dictionary<string, RepoInfo>(StringComparer.OrdinalIgnoreCase);
        var seedFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var topic in _options.Topics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _client.SearchRepositoriesByTopicAsync(
                    topic, _options.MinStars, _options.MaxReposPerTopic, cancellationToken);

                foreach (var repo in result?.Items ?? [])
                {
                    byFullName[repo.FullName] = ToRepoInfo(repo);
                }

                _logger.LogInformation("Topic '{Topic}' search returned {Count} repos", topic, result?.Items.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search for topic '{Topic}' failed; continuing with other topics", topic);
            }
        }

        foreach (var seed in _options.SeedRepos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var repo = byFullName.TryGetValue(seed, out var existing) ? existing : null;
                if (repo is null)
                {
                    var dto = await _client.GetRepositoryAsync(seed, cancellationToken);
                    if (dto is null)
                    {
                        continue;
                    }

                    repo = ToRepoInfo(dto);
                    byFullName[repo.FullName] = repo;
                }

                seedFullNames.Add(repo.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fetching seed repo '{Repo}' failed; skipping it", seed);
            }
        }

        var candidateCount = byFullName.Count;
        var selected = ApplyMaxRepos(byFullName.Values, seedFullNames);

        if (selected.Count < candidateCount)
        {
            _logger.LogInformation(
                "本次處理 {Selected} / 候選 {Candidates} 個 repo（受 MaxRepos={MaxRepos} 上限，依熱度取高分優先，SeedRepos 保底納入）",
                selected.Count, candidateCount, _options.MaxRepos);
        }
        else
        {
            _logger.LogInformation("本次處理 {Selected} / 候選 {Candidates} 個 repo（未達 MaxRepos={MaxRepos} 上限）",
                selected.Count, candidateCount, _options.MaxRepos);
        }

        return selected;
    }

    /// <summary>
    /// Caps the candidate set to MaxRepos. SeedRepos are always kept (they were explicitly configured);
    /// the remaining budget is filled by the highest-heat (stars desc, then most recently pushed) of
    /// the rest — never a random/arbitrary cut.
    /// </summary>
    private List<RepoInfo> ApplyMaxRepos(IEnumerable<RepoInfo> candidates, HashSet<string> seedFullNames)
    {
        var guaranteed = new List<RepoInfo>();
        var rest = new List<RepoInfo>();

        foreach (var repo in candidates)
        {
            (seedFullNames.Contains(repo.FullName) ? guaranteed : rest).Add(repo);
        }

        var budget = Math.Max(0, _options.MaxRepos - guaranteed.Count);
        var topRest = rest
            .OrderByDescending(r => r.Stars)
            .ThenByDescending(r => r.PushedAtUtc)
            .Take(budget);

        return guaranteed.Concat(topRest).ToList();
    }

    private static RepoInfo ToRepoInfo(RepoDto repo) => new()
    {
        FullName = repo.FullName,
        HtmlUrl = repo.HtmlUrl,
        Stars = repo.StargazersCount,
        PushedAtUtc = repo.PushedAt,
        DefaultBranch = repo.DefaultBranch,
    };
}
