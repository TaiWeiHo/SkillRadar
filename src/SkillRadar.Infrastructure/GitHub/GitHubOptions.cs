namespace SkillRadar.Infrastructure.GitHub;

/// <summary>Bound from the "SkillRadar:GitHub" configuration section. No secrets live here.</summary>
public sealed class GitHubOptions
{
    public const string SectionName = "SkillRadar:GitHub";

    /// <summary>GitHub topics to search for, e.g. "claude-skills", "claude-code-skills", "agent-skills".</summary>
    public List<string> Topics { get; init; } = new();

    /// <summary>Extra repos (owner/name) always included regardless of what search turns up.</summary>
    public List<string> SeedRepos { get; init; } = new();

    /// <summary>Minimum stars a repo needs to be considered during topic search.</summary>
    public int MinStars { get; init; } = 5;

    /// <summary>Max repos to pull per topic search (search API caps at 100/page).</summary>
    public int MaxReposPerTopic { get; init; } = 50;

    /// <summary>
    /// Hard cap on how many repos get harvested in a single run, across all topics + seeds combined
    /// (after dedup). Without this, 3 topics × MaxReposPerTopic candidates can mean 1000+ Contents API
    /// calls in Harvest and a run that takes 30+ minutes — this bounds a single run's worst case.
    /// SeedRepos always count toward the budget but are never dropped for low heat; the rest of the
    /// budget goes to the highest-heat (stars, then freshness) search results.
    /// </summary>
    public int MaxRepos { get; init; } = 100;

    /// <summary>
    /// Hard cap on how many SKILL.md files get harvested per repo. A single repo with dozens of
    /// skills (e.g. a monorepo of agent tooling) can otherwise dominate a run's API budget on its own.
    /// </summary>
    public int MaxSkillsPerRepo { get; init; } = 20;

    /// <summary>
    /// How many repos GitHubHarvester processes concurrently (bounded via SemaphoreSlim). This is
    /// what actually bounds wall-clock time — MaxRepos/MaxSkillsPerRepo only bound total API call
    /// COUNT, which at sequential (1x) concurrency still means minutes of pure latency. Kept
    /// deliberately low and explicit (not "as many as possible") because GitHub's secondary/abuse
    /// rate limit reacts to request burstiness, not just raw volume — this is the dial to turn down
    /// if a run starts seeing repeated 403s across many repos at once.
    /// </summary>
    public int MaxConcurrency { get; init; } = 6;

    public string ApiBaseUrl { get; init; } = "https://api.github.com";
}
