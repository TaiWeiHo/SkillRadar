namespace SkillRadar.Infrastructure.Scoring;

/// <summary>Bound from the "SkillRadar:Scoring:Heat" configuration section.</summary>
public sealed class HeatScoringOptions
{
    public const string SectionName = "SkillRadar:Scoring:Heat";

    /// <summary>Weight applied to log10(stars + 1).</summary>
    public double StarWeight { get; init; } = 4.0;

    /// <summary>Weight applied to the freshness decay term (1.0 = pushed today, ~0 = long stale).</summary>
    public double FreshnessWeight { get; init; } = 3.0;

    /// <summary>Number of days for the freshness score to halve.</summary>
    public double FreshnessHalfLifeDays { get; init; } = 30.0;

    /// <summary>Weight applied to log10(skillCountInRepo + 1) — rewards repos actively curating multiple skills.</summary>
    public double DensityWeight { get; init; } = 1.0;
}
