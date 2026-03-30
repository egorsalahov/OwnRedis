using System.Net.Http.Json;
using OwnRedis.Core;
using OwnRedis.Core.Inrerfaces;

namespace OwnRedis.Client;

public class OwnRedisClient : ICacheMethodsService
{
    private readonly HttpClient _httpClient;

    public OwnRedisClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CacheObject?> GetAsync(string key)
    {
        var response = await _httpClient.GetAsync($"/api/cache/{key}");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        return await response.Content.ReadFromJsonAsync<CacheObject>();
    }

    public async Task SetAsync(string key, CacheObject value, double secondsTTL)
    {
        var request = new RequestObject
        {
            Key = key,
            Value = value,
            secondsTTL = secondsTTL
        };

        var response = await _httpClient.PostAsJsonAsync("/api/cache", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CacheObject?> DeleteAsync(string key)
    {
        var response = await _httpClient.DeleteAsync($"/api/cache/{key}");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        return await response.Content.ReadFromJsonAsync<CacheObject>();
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var response = await _httpClient.GetAsync($"/api/cache/exists/{key}");
        
        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<ExistsResponse>();
        return result?.Exists ?? false;
    }

    // Вспомогательный класс для десериализации ответа Exists
    private class ExistsResponse { public bool Exists { get; set; } }
    
    public async Task<object?> GetAdminStatsAsync()
    {
        var response = await _httpClient.GetAsync("/api/admin/stats");
    
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<object>();
    }
}