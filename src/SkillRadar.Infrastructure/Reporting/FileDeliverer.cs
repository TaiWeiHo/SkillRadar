using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.Reporting;

/// <summary>v1 delivery target: writes the digest to "{OutputDirectory}/yyyy-MM-dd.md". v2 can add Notion/LINE as sibling IDeliverer implementations.</summary>
public sealed class FileDeliverer : IDeliverer
{
    private readonly MarkdownReportOptions _options;
    private readonly ILogger<FileDeliverer> _logger;

    public FileDeliverer(IOptions<MarkdownReportOptions> options, ILogger<FileDeliverer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task DeliverAsync(DigestReport report, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.OutputDirectory);

        var fileName = $"{report.ReportDate:yyyy-MM-dd}.md";
        var path = Path.Combine(_options.OutputDirectory, fileName);

        await File.WriteAllTextAsync(path, report.MarkdownContent, cancellationToken);

        _logger.LogInformation("Digest written to {Path}", Path.GetFullPath(path));
    }
}
