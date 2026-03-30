using Microsoft.EntityFrameworkCore;

namespace OwnRedis.Server.Database;

public class RedisDbContext : DbContext
{
    public DbSet<DbCacheItem> CacheItems { get; set; }
    
    public RedisDbContext(DbContextOptions<RedisDbContext> options) : base(options) { }
}