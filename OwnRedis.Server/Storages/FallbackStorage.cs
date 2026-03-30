using System.Collections.Concurrent;

namespace OwnRedis.Core;

public static class FallbackStorage
{
    public static ConcurrentDictionary<string, CacheObject> Cache = new();
}