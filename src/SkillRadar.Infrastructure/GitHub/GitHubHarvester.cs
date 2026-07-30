using Microsoft.Extensions.Logging;
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
    private readonly ILogger<GitHubHarvester> _logger;

    public GitHubHarvester(GitHubApiClient client, ILogger<GitHubHarvester> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SkillRecord>> HarvestAsync(
        IReadOnlyList<RepoInfo> repositories, CancellationToken cancellationToken = default)
    {
        var results = new List<SkillRecord>();

        foreach (var repo in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var skills = await HarvestRepoAsync(repo, cancellationToken);
                results.AddRange(skills);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Harvesting repo '{Repo}' failed; skipping it", repo.FullName);
            }
        }

        return results;
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

        var skillPaths = tree.Tree
            .Where(item => item.Type == "blob" && item.Path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Path)
            .ToList();

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
                    SkillCountInRepo = skillPaths.Count,
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
