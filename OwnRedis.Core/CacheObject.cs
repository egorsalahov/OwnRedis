namespace OwnRedis.Core;

public class CacheObject
{
    public object Value { get; set; }
    public DateTimeOffset TTL { get; set; }
}