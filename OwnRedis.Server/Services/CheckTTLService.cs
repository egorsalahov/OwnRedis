using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OwnRedis.Core;
using OwnRedis.Server.Database;
using Microsoft.EntityFrameworkCore;

//TODO: Solid, каждый уровень == свой BackgroundService

public class CheckTTLService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CheckTTLService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow; //TODO: отдельный сервис для получения даты

            //RAM -> Fallback
            foreach (var key in Storage.Cache.Keys.ToList())
            {
                if (Storage.Cache.TryGetValue(key, out var val) && now >= val.TTL)
                {
                    if (Storage.Cache.TryRemove(key, out _))
                    {
                        val.TTL = now.AddSeconds(15); //TODO: settings
                        FallbackStorage.Cache[key] = val;
                        AdminLogger.Log($"Background: '{key}' -> Fallback");
                    }
                }
            }

            //Fallback -> удаляем из памяти приложени
            foreach (var key in FallbackStorage.Cache.Keys.ToList())
            {
                if (FallbackStorage.Cache.TryGetValue(key, out var val) && now >= val.TTL)
                {
                    FallbackStorage.Cache.TryRemove(key, out _);
                    AdminLogger.Log($"Background: '{key}' удален из памяти (остался в БД)");
                }
            }

            //бд, если вышло время ttlbd
            await CleanExpiredDatabaseItemsAsync(now);

            await Task.Delay(5000, stoppingToken); //5 секунд
        }
    }

    //дополнительный метод для бд
    private async Task CleanExpiredDatabaseItemsAsync(DateTimeOffset now)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();

            //записи где bdttl истек
            var expiredInDb = await db.CacheItems
                .Where(x => x.TTL <= now)
                .ToListAsync();

            if (expiredInDb.Any())
            {
                db.CacheItems.RemoveRange(expiredInDb);
                await db.SaveChangesAsync();
                AdminLogger.Log($"Background: Очищено {expiredInDb.Count} просроченных записей в БД");
            }
        }
        catch (Exception ex)
        {
            AdminLogger.Log($"Ошибка чистки БД: {ex.Message}");
        }
    }
}