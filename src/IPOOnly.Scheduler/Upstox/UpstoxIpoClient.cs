using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace IPOOnly.Scheduler.Upstox;

public sealed class UpstoxIpoClient(HttpClient httpClient, IOptions<UpstoxOptions> options) : IUpstoxIpoClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public Task<UpstoxListResponse> GetIpoPageAsync(
        string status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"ipos?status={Uri.EscapeDataString(status)}&page_number={pageNumber}&records={pageSize}";
        return GetAsync<UpstoxListResponse>(path, cancellationToken);
    }

    public Task<UpstoxDetailResponse> GetIpoDetailAsync(string id, CancellationToken cancellationToken)
    {
        return GetAsync<UpstoxDetailResponse>($"ipos/{Uri.EscapeDataString(id)}", cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AnalyticsToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Upstox returned an empty response.");
    }
}
