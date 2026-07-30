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

    public string ApiBaseUrl { get; init; } = "https://api.github.com";
}
