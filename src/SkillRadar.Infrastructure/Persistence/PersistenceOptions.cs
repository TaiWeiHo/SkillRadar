namespace SkillRadar.Infrastructure.Persistence;

/// <summary>Bound from the "SkillRadar:Persistence" configuration section.</summary>
public sealed class PersistenceOptions
{
    public const string SectionName = "SkillRadar:Persistence";

    /// <summary>
    /// Path to the SQLite database file, relative to the working directory unless rooted. This is
    /// the file the daily GitHub Actions workflow commits back to the repo so incremental diffing
    /// survives across otherwise-stateless CI runs — keep it under a tracked, non-build path.
    /// </summary>
    public string DatabasePath { get; init; } = "state/skills.db";
}
