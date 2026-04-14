using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OwnRedis.Core;
using OwnRedis.Server.Database;

namespace OwnRedis.Tests;

//TODO: тесты по многоточке

[TestFixture]
public class DefaultMethodsServiceTests
{
    private DefaultMethodsService _service;
    private RedisDbContext _dbContext;
    private IServiceScopeFactory _scopeFactory;

    [SetUp]
    public void Setup()
    {
        //In-Memory бд
        var options = new DbContextOptionsBuilder<RedisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Уникальное имя для каждого теста
            .Options;

        _dbContext = new RedisDbContext(options);

        //di
        var services = new ServiceCollection();
        services.AddSingleton(_dbContext);
        var serviceProvider = services.BuildServiceProvider();
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        //создаем объект сервиса
        _service = new DefaultMethodsService(_scopeFactory);

        //чистим кэш перед каждым тестом
        Storage.Cache.Clear();
        FallbackStorage.Cache.Clear();
    }

    [TearDown]
    public void Cleanup()
    {
        _dbContext.Dispose();
    }

    //проверка что сохраняем в RAM и БД при SetAsync
    [Test]
    public async Task SetAsync_ShouldSaveInRamAndDatabase()
    {
        // Arrange
        var key = "test_key";
        var cacheObj = new CacheObject { Value = "hello", TTL = DateTimeOffset.UtcNow.AddMinutes(1) };

        // Act
        await _service.SetAsync(key, cacheObj, 60);

        // Assert
        Assert.Multiple(() =>
        {
            // Проверка RAM
            Assert.That(Storage.Cache.ContainsKey(key), Is.True);
            // Проверка БД
            var dbItem = _dbContext.CacheItems.FirstOrDefault(x => x.Key == key);
            Assert.That(dbItem, Is.Not.Null);
            Assert.That(dbItem.Key, Is.EqualTo(key));
        });
    }

    //когда берем данные, которые есть в RAM, должны вернуть их оттуда, не обращаясь к БД
    [Test]
    public async Task GetAsync_WhenKeyExistsInRam_ShouldReturnFromRam()
    {
        // Arrange
        var key = "ram_key";
        var cacheObj = new CacheObject { Value = "ram_value", TTL = DateTimeOffset.UtcNow.AddMinutes(1) };
        Storage.Cache[key] = cacheObj;

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.ToString(), Is.EqualTo("ram_value"));
    }

    //получаем данные которые есть только в БД, должны вернуть их и поднять в RAM
    [Test]
    public async Task GetAsync_WhenKeyInDatabaseOnly_ShouldRestoreToRam()
    {
        // Arrange
        var key = "db_key";
        var dbItem = new DbCacheItem
        {
            Key = key,
            ValueJson = "\"from_db\"",
            TTL = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        _dbContext.CacheItems.Add(dbItem);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(Storage.Cache.ContainsKey(key), Is.True, "Данные должны подняться из БД в оперативку");
        });
    }

    //при удалении данных должны удалиться и из RAM, и из БД
    [Test]
    public async Task DeleteAsync_ShouldRemoveFromEverywhere()
    {
        // Arrange
        var key = "kill_me";
        Storage.Cache[key] = new CacheObject { Value = 1 };
        _dbContext.CacheItems.Add(new DbCacheItem { Key = key, ValueJson = "1", TTL = DateTimeOffset.UtcNow.AddMinutes(1) });
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.DeleteAsync(key);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(Storage.Cache.ContainsKey(key), Is.False);
            Assert.That(_dbContext.CacheItems.Any(x => x.Key == key), Is.False);
        });
    }

    

    //возвращение из Fallback в Main при получении клиентом объекта из Fallback
    [Test]
    public async Task GetAsync_WhenKeyInFallback_ShouldRestoreToRam()
    {
        // Arrange
        var key = "fallback_key";
        var cacheObj = new CacheObject { Value = "restored_data", TTL = DateTimeOffset.UtcNow.AddSeconds(10) };
        FallbackStorage.Cache[key] = cacheObj;

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value.ToString(), Is.EqualTo("restored_data"));
            Assert.That(Storage.Cache.ContainsKey(key), Is.True, "Объект должен был вернуться в основную память");
        });
    }

    //null когда данных нет ни в RAM, ни в Fallback, ни в БД
    [Test]
    public async Task GetAsync_WhenKeyDoesNotExistAnywhere_ShouldReturnNull()
    {
        // Arrange
        var key = "non_existent_key";

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        Assert.That(result, Is.Null, "Если данных нет ни на одном уровне, возвращаем null");
    }

    // После запроса данных из БД -> RAM с OriginalTTLSeconds
    [Test]
    public async Task GetAsync_WhenRestoringFromDb_ShouldUseOriginalTTL()
    {
        // Arrange
        var key = "original_ttl_test";
        double originalSeconds = 300; // 5 минут
        var dbItem = new DbCacheItem
        {
            Key = key,
            ValueJson = "\"some_value\"",
            TTL = DateTimeOffset.UtcNow.AddMinutes(1), // В базе осталось жить 1 мин
            OriginalTTLSeconds = originalSeconds
        };
        _dbContext.CacheItems.Add(dbItem);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);

            // Проверяем, что новый TTL в RAM — это примерно "сейчас + 5 минут", 
            // а не "сейчас + 1 минута" (которая была в БД)
            var expectedTtl = DateTimeOffset.UtcNow.AddSeconds(originalSeconds);
            var diff = (result.TTL - expectedTtl).Duration().TotalSeconds;

            Assert.That(diff, Is.LessThan(2), "Объект должен получить новую жизнь на полные 300 секунд");

            // Проверка, что в БД тоже обновился TTL (запас +10 минут к новому TTL)
            var updatedDbItem = _dbContext.CacheItems.First(x => x.Key == key);
            Assert.That(updatedDbItem.TTL > result.TTL, Is.True, "TTL в базе должен быть обновлен с запасом");
        });
    }

}