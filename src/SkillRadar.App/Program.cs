using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Serilog;
using SkillRadar.App;
using SkillRadar.Core.Abstractions;
using SkillRadar.Infrastructure.GitHub;
using SkillRadar.Infrastructure.Persistence;
using SkillRadar.Infrastructure.Reporting;
using SkillRadar.Infrastructure.Scoring;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json + appsettings.{Environment}.json + environment variables + user-secrets (Development)
// are wired in automatically by Host.CreateApplicationBuilder.
builder.Logging.ClearProviders();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Services.AddSerilog();

builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.SectionName));
builder.Services.Configure<HeatScoringOptions>(builder.Configuration.GetSection(HeatScoringOptions.SectionName));
builder.Services.Configure<LlmRelevanceOptions>(builder.Configuration.GetSection(LlmRelevanceOptions.SectionName));
builder.Services.Configure<MarkdownReportOptions>(builder.Configuration.GetSection(MarkdownReportOptions.SectionName));

builder.Services.AddDbContext<SkillRadarDbContext>((sp, options) =>
{
    var persistence = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
    var dbPath = Path.GetFullPath(persistence.DatabasePath);
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    options.UseSqlite($"Data Source={dbPath}");
});

builder.Services
    .AddHttpClient<GitHubApiClient>()
    // Custom pipeline (retry + timeout only) instead of AddStandardResilienceHandler: the standard
    // handler also adds a circuit breaker, and that state is shared across every concurrent request
    // on this HttpClient. With GitHubHarvester now running MaxConcurrency repos in flight, a shared
    // breaker would let one repo's cluster of secondary-rate-limit 403s trip a fail-fast state that
    // then also hits every *other* concurrent repo's requests — exactly the "one repo's backoff
    // shouldn't stall the whole batch" failure mode this needs to avoid. Retry and Timeout below are
    // both scoped to a single request's own execution, so they stay isolated per-repo/per-file.
    .AddResilienceHandler("github", pipeline =>
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(30)); // overall budget for one call, across all its retries
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            // GitHub signals both the primary rate limit (403) and secondary/abuse limits (403 or
            // 429) the same way a normal server error would be retried; the default predicate only
            // covers 5xx/408/429, so we widen it to include 403 as well.
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is not null ||
                args.Outcome.Result?.StatusCode is HttpStatusCode.Forbidden
                    or HttpStatusCode.TooManyRequests
                    or >= HttpStatusCode.InternalServerError),
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(10)); // per-attempt budget so one hung attempt can't eat the whole 30s
    });

builder.Services.AddScoped<IDiscoverySource, GitHubDiscoverySource>();
builder.Services.AddScoped<IHarvester, GitHubHarvester>();
builder.Services.AddScoped<IStateStore, SqliteStateStore>();
builder.Services.AddScoped<IScorer, HeatScorer>(); // swap for LlmRelevanceScorer once v2 is implemented
builder.Services.AddScoped<IReporter, MarkdownReporter>();
builder.Services.AddScoped<IDeliverer, FileDeliverer>();
builder.Services.AddScoped<PipelineRunner>();

var host = builder.Build();

try
{
    using var scope = host.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<SkillRadarDbContext>();
    await db.Database.MigrateAsync();

    var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();
    var exitCode = await runner.RunAsync();

    // Microsoft.Data.Sqlite pools connections by default: DbContext.Dispose() returns the native
    // handle to the pool instead of calling sqlite3_close(), so SQLite's own "checkpoint on last
    // connection close" never fires, and the process exiting just abruptly drops the file handle.
    // Without this, committed state/skills.db can silently stay stale — a small day's diff might
    // never cross SQLite's size-based auto-checkpoint threshold either — while the real data sits
    // in state/skills.db-wal, which is (correctly) gitignored and never makes it into the commit.
    await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");

    return exitCode;
}
finally
{
    await Log.CloseAndFlushAsync();
}
