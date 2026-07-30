namespace SkillRadar.Infrastructure.Scoring;

/// <summary>
/// Bound from the "SkillRadar:Scoring:LlmRelevance" configuration section. v2 only — not wired into
/// DI or the pipeline yet. See README "v2 extension points" before implementing.
/// </summary>
public sealed class LlmRelevanceOptions
{
    public const string SectionName = "SkillRadar:Scoring:LlmRelevance";

    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Anthropic model id, e.g. a Haiku-tier model for low-cost batch scoring. Check
    /// https://docs.claude.com for the currently available model ids before setting this —
    /// do not assume the value here is still valid.
    /// </summary>
    public string Model { get; init; } = "claude-haiku-4-5";

    /// <summary>Only the top N heat-scored skills get sent to the LLM, to bound cost per run.</summary>
    public int MaxItemsToScore { get; init; } = 30;
}
