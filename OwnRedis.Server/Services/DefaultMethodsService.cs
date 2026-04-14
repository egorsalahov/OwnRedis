using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwnRedis.Core.Inrerfaces;
using OwnRedis.Server.Database;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OwnRedis.Core;

public class DefaultMethodsService : ICacheMethodsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DefaultMethodsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    //TODO: рефакторинг разделение на методы/уровни
    public async Task<CacheObject?> GetAsync(string key)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. RAM
        if (Storage.Cache.TryGetValue(key, out var ramValue))
        {
            if (ramValue.TTL > now) return ramValue;

            if (Storage.Cache.TryRemove(key, out var expiredValue))
            {
                expiredValue.TTL = now.AddSeconds(15); //TODO: ~settings.json, конфигурация
                FallbackStorage.Cache[key] = expiredValue;
                AdminLogger.Log($"TTL истек: '{key}' перемещен в Fallback");
            }
        }

        // 2. Fallback
        if (FallbackStorage.Cache.TryGetValue(key, out var fallbackValue))
        {
            if (fallbackValue.TTL > now)
            {
                Storage.Cache[key] = fallbackValue;
                return fallbackValue;
            }
            FallbackStorage.Cache.TryRemove(key, out _);
        }

        //TODO: в отдельном сервисе
        // 3. Database
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
            var dbItem = await db.CacheItems.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);

            if (dbItem != null)
            {
                //dbttl 
                if (dbItem.TTL.ToUniversalTime() > now)
                {
                    var rawJson = JsonSerializer.Deserialize<JsonElement>(dbItem.ValueJson);

                    object? processedValue = rawJson.ValueKind switch
                    {
                        JsonValueKind.Array => JsonSerializer.Deserialize<List<object>>(dbItem.ValueJson),
                        JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(dbItem.ValueJson),
                        _ => JsonSerializer.Deserialize<object>(dbItem.ValueJson)
                    };

                    var newRamTTL = now.AddSeconds(dbItem.OriginalTTLSeconds);

                    var cacheObj = new CacheObject
                    {
                        Value = processedValue ?? "empty",
                        TTL = newRamTTL
                    };

                    // Поднимаем в оперативку
                    Storage.Cache[key] = cacheObj;

                    // Обновляем TTL в базе данных (даем новый запас 10 минут от нового TTL)
                    await UpdateDatabaseTtlAsync(key, newRamTTL.AddMinutes(10)); //TODO: ~settings.json, конфигурация
                    

                    AdminLogger.Log($"Восстановление: '{key}' поднят из БД. Новый RAM TTL: {newRamTTL:T}");
                    return cacheObj;
                }

                //протух dbttl
                db.CacheItems.Remove(dbItem);
                await db.SaveChangesAsync();

                AdminLogger.Log($"Очистка: '{key}' окончательно удален из БД (время вышло)");
            }
        }
        return null;
    }

    public async Task SetAsync(string key, CacheObject value, double secondsTTL)
    {
        var now = DateTimeOffset.UtcNow; //TODO: сервис для даты

        //TTL для RAM
        value.TTL = now.AddSeconds(secondsTTL).ToUniversalTime();
        Storage.Cache[key] = value;

        // TTL для базы (???)
        var dbTtl = value.TTL.AddMinutes(10);

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();

            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            var jsonString = JsonSerializer.Serialize(value.Value, options);
            var existing = await db.CacheItems.FirstOrDefaultAsync(x => x.Key == key);

            if (existing != null)
            {
                existing.ValueJson = jsonString;
                existing.TTL = dbTtl;
                existing.OriginalTTLSeconds = secondsTTL;
            }
            else
            {
                await db.CacheItems.AddAsync(new DbCacheItem
                {
                    Key = key,
                    ValueJson = jsonString,
                    TTL = dbTtl,
                    OriginalTTLSeconds = secondsTTL
                });
            }

            await db.SaveChangesAsync();
            AdminLogger.Log($"Set: '{key}' (RAM: {secondsTTL}s, DB запас до {dbTtl:T})");
        }
    }

    // Вспомогательный метод для ttl в БД при GET
    private async Task UpdateDatabaseTtlAsync(string key, DateTimeOffset newDbTtl)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RedisDbContext>();
            var item = await db.CacheItems.FirstOrDefaultAsync(x => x.Key == key);
            if (item != null)
            {
                item.TTL = newDbTtl;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            AdminLogger.Log($"Ошибка обновления TTL в БД для '{key}': {ex.Message}");
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

        var dbItems = await db.CacheItems.AsNoTracking().ToListAsync();
        var allKeysMap = new Dictionary<string, (string Location, DateTimeOffset TTL)>();

        foreach (var item in dbItems)
        {
            allKeysMap[item.Key] = ("Database", item.TTL);
        }

        foreach (var kvp in FallbackStorage.Cache)
        {
            allKeysMap[kvp.Key] = ("Fallback", kvp.Value.TTL);
        }

        foreach (var kvp in Storage.Cache)
        {
            allKeysMap[kvp.Key] = ("RAM", kvp.Value.TTL);
        }

        var allKeysList = allKeysMap.Select(x => new
        {
            key = x.Key,
            location = x.Value.Location,
            ttl = x.Value.TTL
        })
        .OrderBy(x => x.key)
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