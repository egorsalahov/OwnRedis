namespace OwnRedis.Core.Inrerfaces;

public interface ICacheMethodsService
{
    Task<CacheObject?> GetAsync(string key);
    Task SetAsync(string key, CacheObject value, double secondsTLL);
    Task<CacheObject?> DeleteAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<object> GetAdminStatsAsync();
}