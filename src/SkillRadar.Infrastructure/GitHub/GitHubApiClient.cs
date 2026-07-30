using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SkillRadar.Infrastructure.GitHub;

/// <summary>
/// Thin wrapper over the GitHub REST API. We use a plain HttpClient (via IHttpClientFactory) instead of
/// Octokit: the two calls we actually need (Search Repositories, Git Trees recursive, and single-file
/// Contents) are simple enough that a typed HttpClient keeps the dependency surface small and lets us
/// wire GitHub-specific rate-limit/retry behavior directly through Microsoft.Extensions.Http.Resilience
/// without fighting an SDK's own retry/paging abstractions.
/// </summary>
public sealed class GitHubApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubApiClient> _logger;

    public GitHubApiClient(
        HttpClient http, IOptions<GitHubOptions> options, IConfiguration configuration, ILogger<GitHubApiClient> logger)
    {
        _http = http;
        _logger = logger;

        _http.BaseAddress = new Uri(options.Value.ApiBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SkillRadar", "1.0"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        // Read via IConfiguration, not Environment.GetEnvironmentVariable directly: IConfiguration
        // is what actually merges in `dotnet user-secrets` values (only Environment.GetEnvironmentVariable
        // would miss those, since user-secrets are never written to the real process environment) as well
        // as a plain GITHUB_TOKEN env var, in either Development or Production.
        var token = configuration["GITHUB_TOKEN"];
        var tokenLoaded = !string.IsNullOrWhiteSpace(token);
        _logger.LogInformation("token loaded: {TokenLoaded}", tokenLoaded);

        if (tokenLoaded)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _logger.LogWarning("GITHUB_TOKEN is not set; requests will run unauthenticated with much lower rate limits.");
        }
    }

    public async Task<RepoSearchResponse?> SearchRepositoriesByTopicAsync(
        string topic, int minStars, int perPage, CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"topic:{topic} stars:>={minStars}");
        var url = $"search/repositories?q={query}&sort=stars&order=desc&per_page={perPage}";
        var response = await _http.GetAsync(url, ct);
        return await ReadOrLogAsync<RepoSearchResponse>(response, $"search topic:{topic}", ct);
    }

    public async Task<RepoDto?> GetRepositoryAsync(string fullName, CancellationToken ct)
    {
        var response = await _http.GetAsync($"repos/{fullName}", ct);
        return await ReadOrLogAsync<RepoDto>(response, $"get repo {fullName}", ct);
    }

    public async Task<TreeResponse?> GetTreeRecursiveAsync(string fullName, string branch, CancellationToken ct)
    {
        var response = await _http.GetAsync($"repos/{fullName}/git/trees/{branch}?recursive=1", ct);
        return await ReadOrLogAsync<TreeResponse>(response, $"tree {fullName}@{branch}", ct);
    }

    public async Task<string?> GetFileContentAsync(string fullName, string path, string branch, CancellationToken ct)
    {
        var response = await _http.GetAsync($"repos/{fullName}/contents/{path}?ref={Uri.EscapeDataString(branch)}", ct);
        var dto = await ReadOrLogAsync<ContentDto>(response, $"contents {fullName}/{path}", ct);
        if (dto is null || dto.Encoding != "base64" || dto.Content is null)
        {
            return null;
        }

        var bytes = Convert.FromBase64String(dto.Content.Replace("\n", string.Empty));
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<T?> ReadOrLogAsync<T>(HttpResponseMessage response, string what, CancellationToken ct)
    {
        LogRateLimit(response);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GitHub API call failed ({What}): {StatusCode} {ReasonPhrase}",
                what, (int)response.StatusCode, response.ReasonPhrase);
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private void LogRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
        {
            _logger.LogDebug("GitHub rate limit remaining: {Remaining}", remaining.FirstOrDefault());
        }
    }
}

public sealed record RepoSearchResponse(
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("items")] List<RepoDto> Items);

public sealed record RepoDto(
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset PushedAt,
    [property: JsonPropertyName("default_branch")] string DefaultBranch);

public sealed record TreeResponse(
    [property: JsonPropertyName("tree")] List<TreeItemDto> Tree,
    [property: JsonPropertyName("truncated")] bool Truncated);

public sealed record TreeItemDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type);

public sealed record ContentDto(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("encoding")] string? Encoding);
