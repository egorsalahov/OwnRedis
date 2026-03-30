using Microsoft.Extensions.Hosting;
using OwnRedis.Core;

public class CheckTTLService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            
            foreach (var key in Storage.Cache.Keys.ToList())
            {
                if (Storage.Cache.TryGetValue(key, out var val) && now >= val.TTL)
                {
                    if (Storage.Cache.TryRemove(key, out _))
                    {
                        val.TTL = now.AddSeconds(15);
                        FallbackStorage.Cache[key] = val;
                        AdminLogger.Log($"Background: '{key}' -> Fallback");
                    }
                }
            }
            
            foreach (var key in FallbackStorage.Cache.Keys.ToList())
            {
                if (FallbackStorage.Cache.TryGetValue(key, out var val) && now >= val.TTL)
                {
                    if (FallbackStorage.Cache.TryRemove(key, out _))
                    {
                        AdminLogger.Log($"Background: '{key}' окончательно удален");
                    }
                }
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}