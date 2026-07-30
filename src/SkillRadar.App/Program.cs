using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    .AddStandardResilienceHandler(options =>
    {
        // GitHub signals both the primary rate limit (403) and secondary/abuse limits (403 or 429)
        // the same way a normal server error would be retried; the standard predicate only covers
        // 5xx/408/429 by default, so we widen it to include 403 as well.
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is not null ||
            args.Outcome.Result?.StatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.TooManyRequests
                or >= HttpStatusCode.InternalServerError);
        options.Retry.MaxRetryAttempts = 5;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
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
    return exitCode;
}
finally
{
    await Log.CloseAndFlushAsync();
}
