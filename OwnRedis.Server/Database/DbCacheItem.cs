using System.ComponentModel.DataAnnotations;

namespace OwnRedis.Server.Database;

public class DbCacheItem
{
    [Key]
    public string Key { get; set; } = string.Empty;
    
    public string ValueJson { get; set; } = string.Empty;
    
    public DateTimeOffset TTL { get; set; }
}