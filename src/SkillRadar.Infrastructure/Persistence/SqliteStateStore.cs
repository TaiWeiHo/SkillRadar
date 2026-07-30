using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite backed IStateStore. DiffAsync is read-only and side-effect free; PersistAsync is
/// the only method that writes, so the pipeline can diff/score before committing anything to disk.
/// </summary>
public sealed class SqliteStateStore : IStateStore
{
    private readonly SkillRadarDbContext _db;
    private readonly ILogger<SqliteStateStore> _logger;

    public SqliteStateStore(SkillRadarDbContext db, ILogger<SqliteStateStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SkillDiffResult> DiffAsync(
        IReadOnlyList<SkillRecord> currentSkills, CancellationToken cancellationToken = default)
    {
        var keys = currentSkills.Select(s => s.Key).ToList();
        var known = await _db.Skills
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var items = new List<DiffedSkill>(currentSkills.Count);

        foreach (var skill in currentSkills)
        {
            if (!known.TryGetValue(skill.Key, out var existing))
            {
                items.Add(new DiffedSkill { Skill = skill, Status = SkillChangeStatus.New, FirstSeenUtc = now });
                continue;
            }

            var status = existing.ContentHash == skill.ContentHash
                ? SkillChangeStatus.Unchanged
                : SkillChangeStatus.Updated;

            items.Add(new DiffedSkill { Skill = skill, Status = status, FirstSeenUtc = existing.FirstSeenUtc });
        }

        _logger.LogInformation(
            "Diff complete: {New} new, {Updated} updated, {Unchanged} unchanged (of {Total})",
            items.Count(i => i.Status == SkillChangeStatus.New),
            items.Count(i => i.Status == SkillChangeStatus.Updated),
            items.Count(i => i.Status == SkillChangeStatus.Unchanged),
            items.Count);

        return new SkillDiffResult { Items = items };
    }

    public async Task PersistAsync(
        IReadOnlyList<ScoredSkill> scoredSkills, CancellationToken cancellationToken = default)
    {
        var keys = scoredSkills.Select(s => s.Skill.Key).ToList();
        var existing = await _db.Skills
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        foreach (var scored in scoredSkills)
        {
            var skill = scored.Skill;

            if (existing.TryGetValue(skill.Key, out var entity))
            {
                entity.ContentHash = skill.ContentHash;
                entity.Name = skill.Name;
                entity.Description = skill.Description;
                entity.LastScore = scored.Score;
                entity.LastSeenUtc = now;
            }
            else
            {
                _db.Skills.Add(new SkillEntity
                {
                    Key = skill.Key,
                    RepoFullName = skill.RepoFullName,
                    Path = skill.Path,
                    Name = skill.Name,
                    Description = skill.Description,
                    ContentHash = skill.ContentHash,
                    LastScore = scored.Score,
                    FirstSeenUtc = scored.Diffed.FirstSeenUtc,
                    LastSeenUtc = now,
                });
            }
        }

        var saved = await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Persisted state for {Count} skills ({Saved} rows written)", scoredSkills.Count, saved);
    }
}
