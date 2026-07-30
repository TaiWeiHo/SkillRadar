using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkillRadar.Core.Models;
using SkillRadar.Infrastructure.Persistence;
using Xunit;

namespace SkillRadar.Tests.Persistence;

public class SqliteStateStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SkillRadarDbContext _db;
    private readonly SqliteStateStore _store;

    public SqliteStateStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skillradar-test-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<SkillRadarDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SkillRadarDbContext(options);
        _db.Database.EnsureCreated();

        _store = new SqliteStateStore(_db, NullLogger<SqliteStateStore>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools(); // release native file handles before deleting the temp db
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static SkillRecord MakeRecord(string repo, string path, string name, string description, int stars = 10) => new()
    {
        RepoFullName = repo,
        Path = path,
        HtmlUrl = $"https://github.com/{repo}/blob/main/{path}",
        Name = name,
        Description = description,
        ContentHash = SkillRadar.Infrastructure.GitHub.SkillFrontmatterParser.ComputeContentHash(name, description),
        RepoStars = stars,
        RepoPushedAtUtc = DateTimeOffset.UtcNow,
        SkillCountInRepo = 1,
    };

    private static ScoredSkill ToScored(DiffedSkill diffed, double score = 1.0) =>
        new() { Diffed = diffed, Score = score };

    [Fact]
    public async Task DiffAsync_MarksNeverSeenSkill_AsNew()
    {
        var record = MakeRecord("owner/repo", "SKILL.md", "my-skill", "does a thing");

        var diff = await _store.DiffAsync([record]);

        var item = Assert.Single(diff.Items);
        Assert.Equal(SkillChangeStatus.New, item.Status);
    }

    [Fact]
    public async Task DiffAsync_MarksPersistedSkillWithSameHash_AsUnchanged()
    {
        var record = MakeRecord("owner/repo", "SKILL.md", "my-skill", "does a thing");
        var firstDiff = await _store.DiffAsync([record]);
        await _store.PersistAsync([ToScored(firstDiff.Items[0])]);

        var secondDiff = await _store.DiffAsync([record]);

        var item = Assert.Single(secondDiff.Items);
        Assert.Equal(SkillChangeStatus.Unchanged, item.Status);
    }

    [Fact]
    public async Task DiffAsync_MarksPersistedSkillWithChangedDescription_AsUpdated()
    {
        var original = MakeRecord("owner/repo", "SKILL.md", "my-skill", "does a thing");
        var firstDiff = await _store.DiffAsync([original]);
        await _store.PersistAsync([ToScored(firstDiff.Items[0])]);

        var changed = MakeRecord("owner/repo", "SKILL.md", "my-skill", "does a different thing now");
        var secondDiff = await _store.DiffAsync([changed]);

        var item = Assert.Single(secondDiff.Items);
        Assert.Equal(SkillChangeStatus.Updated, item.Status);
    }

    [Fact]
    public async Task DiffAsync_PreservesOriginalFirstSeenDate_AcrossRuns()
    {
        var record = MakeRecord("owner/repo", "SKILL.md", "my-skill", "does a thing");
        var firstDiff = await _store.DiffAsync([record]);
        var originalFirstSeen = firstDiff.Items[0].FirstSeenUtc;
        await _store.PersistAsync([ToScored(firstDiff.Items[0])]);

        await Task.Delay(10); // ensure "now" would differ if FirstSeenUtc were wrongly reset

        var changed = MakeRecord("owner/repo", "SKILL.md", "my-skill", "changed description");
        var secondDiff = await _store.DiffAsync([changed]);

        Assert.Equal(originalFirstSeen, secondDiff.Items[0].FirstSeenUtc);
    }

    [Fact]
    public async Task DiffAsync_TreatsDifferentSkillsInSameBatch_Independently()
    {
        var existing = MakeRecord("owner/repo", "SKILL.md", "existing", "already known");
        var firstDiff = await _store.DiffAsync([existing]);
        await _store.PersistAsync([ToScored(firstDiff.Items[0])]);

        var brandNew = MakeRecord("owner/other-repo", "SKILL.md", "brand-new", "never seen before");
        var diff = await _store.DiffAsync([existing, brandNew]);

        Assert.Equal(SkillChangeStatus.Unchanged, diff.Items.Single(i => i.Skill.Name == "existing").Status);
        Assert.Equal(SkillChangeStatus.New, diff.Items.Single(i => i.Skill.Name == "brand-new").Status);
    }
}
