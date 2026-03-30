using System.Collections.Concurrent;

namespace OwnRedis.Core;

public static class Storage
{
    public static ConcurrentDictionary<string, CacheObject> Cache = new();
}