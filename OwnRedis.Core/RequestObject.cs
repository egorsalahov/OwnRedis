namespace OwnRedis.Core;

public class RequestObject
{
    public string Key { get; set; }
    public CacheObject Value { get; set; }
    public double secondsTTL { get; set; }
}