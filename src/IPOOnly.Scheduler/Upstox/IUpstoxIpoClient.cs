namespace IPOOnly.Scheduler.Upstox;

public interface IUpstoxIpoClient
{
    Task<UpstoxListResponse> GetIpoPageAsync(string status, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<UpstoxDetailResponse> GetIpoDetailAsync(string id, CancellationToken cancellationToken);
}
