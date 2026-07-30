namespace SkillRadar.Core.Models;

/// <summary>A single SKILL.md discovered in a repository, with just enough repo context to score it.</summary>
public sealed record SkillRecord
{
    public required string RepoFullName { get; init; }
    public required string Path { get; init; }
    public required string HtmlUrl { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ContentHash { get; init; }
    public required int RepoStars { get; init; }
    public required DateTimeOffset RepoPushedAtUtc { get; init; }
    public required int SkillCountInRepo { get; init; }

    /// <summary>Stable identity used for diffing and persistence: "{RepoFullName}:{Path}".</summary>
    public string Key => $"{RepoFullName}:{Path}";
}
