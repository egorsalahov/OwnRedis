using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwnRedis.Core.Inrerfaces;
using OwnRedis.Server.Database;

namespace OwnRedis.Core;

public class DefaultMethodsService : ICacheMethodsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DefaultMethodsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<CacheObject?> GetAsync(string key)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. RAM
        if (Storage.Cache.TryGetValue(key, out var ramValue))
        {
            if (ramValue.TTL > now) return ramValue;

            if (Storage.Cache.TryRemove(key, out var expiredValue))
            {
                expiredValue.TTL = now.AddSeconds(15); 
                FallbackStorage.Cache[key] = expiredValue;
                AdminLogger.Log($"TTL истек: '{key}' перемещен из RAM в Fallback");
            }
        }

        // 2. Fallback
        if (FallbackStorage.Cache.TryGetValue(key, out var fallbackValue))
        {
            if (fallbackValue.TTL > now)
            {
                Storage.Cache[key] = fallbackValue;
                AdminLogger.Log($"Восстановление: '{key}' вернулся из Fallback в RAM");
                return fallbackValue;
            }
            FallbackStorage.Cache.TryRemove(key, out _);
            AdminLogger.Log($"Очистка: '{key}' удален из Fallback (время вышло)");
        }

        // 3. Database
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
            var dbItem = await db.CacheItems.FirstOrDefaultAsync(x => x.Key == key);

            if (dbItem != null)
            {
                if (dbItem.TTL > now)
                {
                    var jsonObject = JsonSerializer.Deserialize<object>(dbItem.ValueJson);
                    var cacheObj = new CacheObject { Value = jsonObject, TTL = dbItem.TTL };
                
                    Storage.Cache[key] = cacheObj;
                    AdminLogger.Log($"Восстановление: '{key}' поднят из БД в RAM");
                    return cacheObj;
                }
                db.CacheItems.Remove(dbItem);
                await db.SaveChangesAsync();
                AdminLogger.Log($"Очистка: '{key}' удален из БД (время вышло)");
            }
        }
        return null;
    }

public async Task SetAsync(string key, CacheObject value, double secondsTTL)
{
    var ttl = secondsTTL < 1 
        ? DateTimeOffset.UtcNow.AddSeconds(10) 
        : DateTimeOffset.UtcNow.AddSeconds(secondsTTL);
    
    value.TTL = ttl;

    //cache
    Storage.Cache[key] = value;

    //БД
    using (var scope = _scopeFactory.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
        
        var jsonString = JsonSerializer.Serialize(value.Value);

        var existing = await db.CacheItems.FirstOrDefaultAsync(x => x.Key == key);

        if (existing != null)
        {
            existing.ValueJson = jsonString;
            existing.TTL = ttl;
        }
        else
        {
            await db.CacheItems.AddAsync(new DbCacheItem
            {
                Key = key,
                ValueJson = jsonString,
                TTL = ttl
            });
        }
        await db.SaveChangesAsync();
    }
}

    public async Task<CacheObject?> DeleteAsync(string key)
    {
        Storage.Cache.TryRemove(key, out var ramVal);
        FallbackStorage.Cache.TryRemove(key, out _);

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
            var dbItem = await db.CacheItems.FirstOrDefaultAsync(x => x.Key == key);
            if (dbItem != null)
            {
                db.CacheItems.Remove(dbItem);
                await db.SaveChangesAsync();
            }
        }

        return ramVal;
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (Storage.Cache.ContainsKey(key)) return true;
        
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
            return await db.CacheItems.AnyAsync(x => x.Key == key && x.TTL > DateTimeOffset.UtcNow);
        }
    }
    
    public async Task<object> GetAdminStatsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();

        // Собираем ключи из RAM
        var ramKeys = Storage.Cache.Select(kvp => new 
        { 
            key = kvp.Key, 
            location = "RAM", 
            ttl = kvp.Value.TTL 
        });

        // Собираем ключи из Fallback
        var fallbackKeys = FallbackStorage.Cache.Select(kvp => new 
        { 
            key = kvp.Key, 
            location = "Fallback", 
            ttl = kvp.Value.TTL 
        });

        // Собираем ключи из БД
        var dbItems = await db.CacheItems.ToListAsync();
        var dbKeys = dbItems.Select(x => new 
        { 
            key = x.Key, 
            location = "Database", 
            ttl = x.TTL
        });

        // Объединяем всё. Если ключ есть и там и там, берем первый (из RAM)
        var allKeysList = ramKeys
            .Concat(fallbackKeys)
            .Concat(dbKeys)
            .GroupBy(x => x.key)
            .Select(g => g.First())
            .ToList();

        return new
        {
            storageCount = Storage.Cache.Count,
            fallbackCount = FallbackStorage.Cache.Count,
            databaseCount = dbItems.Count,
            allKeys = allKeysList,
            logs = AdminLogger.Logs.ToArray().Reverse(),
            memoryUsage = AdminLogger.GetMemoryUsage() 
        };
    }
}