namespace IPOOnly.Scheduler.Upstox;

public interface IUpstoxIpoClient
{
    Task<UpstoxApiResponse<UpstoxListResponse>> GetIpoPageAsync(string status, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<UpstoxApiResponse<UpstoxDetailResponse>> GetIpoDetailAsync(string id, CancellationToken cancellationToken);
}
