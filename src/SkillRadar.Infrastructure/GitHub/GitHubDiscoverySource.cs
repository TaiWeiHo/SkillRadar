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
            if (byFullName.ContainsKey(seed))
            {
                continue;
            }

            try
            {
                var repo = await _client.GetRepositoryAsync(seed, cancellationToken);
                if (repo is not null)
                {
                    byFullName[repo.FullName] = ToRepoInfo(repo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fetching seed repo '{Repo}' failed; skipping it", seed);
            }
        }

        return byFullName.Values.ToList();
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
