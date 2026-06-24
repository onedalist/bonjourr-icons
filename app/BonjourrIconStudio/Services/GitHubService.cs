using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BonjourrIconStudio.Models;

namespace BonjourrIconStudio.Services;

public sealed record GitHubUploadResult(bool Success, bool AlreadyExists, string Message, string? PublicUrl = null);

public sealed class GitHubService
{
    private readonly HttpClient _httpClient;

    public GitHubService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(45)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BonjourrIconStudio/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        string token,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, RepositoryEndpoint(settings), token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
            return (true, $"Подключено к {settings.RepositoryOwner}/{settings.RepositoryName}");

        return (false, await DescribeErrorAsync(response));
    }

    public async Task<GitHubUploadResult> UploadAsync(
        string token,
        AppSettings settings,
        string localPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(localPath);
        var repositoryPath = string.Join('/', new[] { settings.RepositoryFolder.Trim('/'), fileName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var endpoint = ContentEndpoint(settings, repositoryPath);

        string? existingSha = null;
        using (var getRequest = CreateRequest(HttpMethod.Get, $"{endpoint}?ref={Uri.EscapeDataString(settings.Branch)}", token))
        using (var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken))
        {
            if (getResponse.IsSuccessStatusCode)
            {
                var existing = JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(cancellationToken));
                existingSha = existing?["sha"]?.GetValue<string>();
                if (!overwrite)
                    return new GitHubUploadResult(false, true, $"Файл {fileName} уже существует.");
            }
            else if (getResponse.StatusCode != HttpStatusCode.NotFound)
            {
                return new GitHubUploadResult(false, false, await DescribeErrorAsync(getResponse));
            }
        }

        var body = new JsonObject
        {
            ["message"] = existingSha is null
                ? $"Add {fileName} from Bonjourr Icon Studio"
                : $"Update {fileName} from Bonjourr Icon Studio",
            ["content"] = Convert.ToBase64String(await File.ReadAllBytesAsync(localPath, cancellationToken)),
            ["branch"] = settings.Branch
        };
        if (existingSha is not null) body["sha"] = existingSha;

        using var putRequest = CreateRequest(HttpMethod.Put, endpoint, token);
        putRequest.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var putResponse = await _httpClient.SendAsync(putRequest, cancellationToken);

        if (!putResponse.IsSuccessStatusCode)
            return new GitHubUploadResult(false, false, await DescribeErrorAsync(putResponse));

        var publicUrl = BuildGitHubPagesUrl(settings, repositoryPath);
        return new GitHubUploadResult(true, false, "Загружено", publicUrl);
    }

    internal static string BuildGitHubPagesUrl(AppSettings settings, string repositoryPath)
    {
        var owner = settings.RepositoryOwner.Trim().ToLowerInvariant();
        var repository = settings.RepositoryName.Trim().Trim('/');
        var encodedRepository = Uri.EscapeDataString(repository);
        var encodedPath = string.Join('/', repositoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString));

        return $"https://{owner}.github.io/{encodedRepository}/{encodedPath}";
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, string token)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return request;
    }

    private static string RepositoryEndpoint(AppSettings settings) =>
        $"repos/{Uri.EscapeDataString(settings.RepositoryOwner)}/{Uri.EscapeDataString(settings.RepositoryName)}";

    private static string ContentEndpoint(AppSettings settings, string path)
    {
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return $"{RepositoryEndpoint(settings)}/contents/{encodedPath}";
    }

    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var message = json?["message"]?.GetValue<string>();
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "GitHub отклонил токен. Проверьте токен и срок его действия.",
                HttpStatusCode.Forbidden => "Недостаточно прав. Для репозитория требуется Contents: Read and write.",
                HttpStatusCode.NotFound => "Репозиторий или ветка не найдены либо токен не имеет к ним доступа.",
                _ => $"GitHub: {message ?? response.ReasonPhrase} ({(int)response.StatusCode})"
            };
        }
        catch
        {
            return $"GitHub вернул ошибку {(int)response.StatusCode}: {response.ReasonPhrase}";
        }
    }
}
