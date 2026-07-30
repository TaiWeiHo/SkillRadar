using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.GitHub;

/// <summary>
/// For each candidate repo, lists the tree recursively to find every SKILL.md, then reads just the
/// frontmatter of each one via the Contents API. Never downloads a full repo. A failure on one repo
/// (bad tree, missing file, unparsable frontmatter) is logged and skipped — it never aborts the batch.
/// </summary>
public sealed class GitHubHarvester : IHarvester
{
    private readonly GitHubApiClient _client;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubHarvester> _logger;

    public GitHubHarvester(GitHubApiClient client, IOptions<GitHubOptions> options, ILogger<GitHubHarvester> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SkillRecord>> HarvestAsync(
        IReadOnlyList<RepoInfo> repositories, CancellationToken cancellationToken = default)
    {
        // Bounded concurrency across repos is what actually controls wall-clock time (MaxRepos /
        // MaxSkillsPerRepo only bound call COUNT). Kept deliberately low via MaxConcurrency rather
        // than unbounded Task.WhenAll, since GitHub's secondary/abuse rate limit reacts to request
        // burstiness — each repo still fetches its own files sequentially, so at most MaxConcurrency
        // HTTP calls are in flight at once, not MaxConcurrency × MaxSkillsPerRepo.
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));

        var tasks = repositories.Select(repo => HarvestRepoIsolatedAsync(repo, gate, cancellationToken));
        var perRepoResults = await Task.WhenAll(tasks);

        return perRepoResults.SelectMany(r => r).ToList();
    }

    /// <summary>
    /// Wraps HarvestRepoAsync with the concurrency gate and this repo's own try/catch, so one repo's
    /// failure (including a Polly retry chain exhausting itself) never propagates into the
    /// Task.WhenAll and never affects any other repo's task — same isolation guarantee as the
    /// original sequential loop, just running up to MaxConcurrency of these at once.
    /// </summary>
    private async Task<List<SkillRecord>> HarvestRepoIsolatedAsync(RepoInfo repo, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await HarvestRepoAsync(repo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Harvesting repo '{Repo}' failed; skipping it", repo.FullName);
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<SkillRecord>> HarvestRepoAsync(RepoInfo repo, CancellationToken ct)
    {
        var tree = await _client.GetTreeRecursiveAsync(repo.FullName, repo.DefaultBranch, ct);
        if (tree is null)
        {
            return [];
        }

        if (tree.Truncated)
        {
            _logger.LogWarning("Tree for '{Repo}' was truncated by the GitHub API; some SKILL.md files may be missed", repo.FullName);
        }

        var allSkillPaths = tree.Tree
            .Where(item => item.Type == "blob" && item.Path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Path)
            .ToList();

        // No per-file heat signal exists before fetching content, so there's nothing meaningful to
        // rank by here — truncate deterministically (tree order) rather than randomly. SkillCountInRepo
        // below still reflects the true total, so HeatScorer's density signal isn't skewed by this cap.
        var skillPaths = allSkillPaths.Count > _options.MaxSkillsPerRepo
            ? allSkillPaths.Take(_options.MaxSkillsPerRepo).ToList()
            : allSkillPaths;

        if (skillPaths.Count < allSkillPaths.Count)
        {
            _logger.LogInformation(
                "Repo '{Repo}': 處理 {Selected} / 候選 {Total} 個 SKILL.md（受 MaxSkillsPerRepo={Max} 上限）",
                repo.FullName, skillPaths.Count, allSkillPaths.Count, _options.MaxSkillsPerRepo);
        }

        var records = new List<SkillRecord>();

        foreach (var path in skillPaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content = await _client.GetFileContentAsync(repo.FullName, path, repo.DefaultBranch, ct);
                if (content is null)
                {
                    continue;
                }

                var parsed = SkillFrontmatterParser.Parse(content);
                if (parsed is null)
                {
                    _logger.LogDebug("No parsable frontmatter in {Repo}/{Path}", repo.FullName, path);
                    continue;
                }

                var (name, description) = parsed.Value;
                records.Add(new SkillRecord
                {
                    RepoFullName = repo.FullName,
                    Path = path,
                    HtmlUrl = $"{repo.HtmlUrl}/blob/{repo.DefaultBranch}/{path}",
                    Name = name,
                    Description = description,
                    ContentHash = SkillFrontmatterParser.ComputeContentHash(name, description),
                    RepoStars = repo.Stars,
                    RepoPushedAtUtc = repo.PushedAtUtc,
                    SkillCountInRepo = allSkillPaths.Count,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading {Repo}/{Path} failed; skipping this skill", repo.FullName, path);
            }
        }

        return records;
    }
}
