using Microsoft.EntityFrameworkCore;
using OwnRedis.Core;
using OwnRedis.Core.Inrerfaces;
using OwnRedis.Server.Database;
//TODO: нужна-ли пользователю БД как 3 слой

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddSingleton<ICacheMethodsService, DefaultMethodsService>();
builder.Services.AddHostedService<CheckTTLService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<RedisDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

app.UseDefaultFiles(); // ищет index.html
app.UseStaticFiles(); //для админки

//Get
app.MapGet("/api/cache/{key}", async (string key, ICacheMethodsService service) => 
{
    var result = await service.GetAsync(key);
    if (result == null) return Results.NotFound();
    return Results.Ok(result);
});

//Set
app.MapPost("/api/cache", async (RequestObject request, ICacheMethodsService service) =>
{
    if (string.IsNullOrWhiteSpace(request.Key))
    {
        return Results.BadRequest("Ключ не может быть пустым");
    }

    // Создаем CacheObject из данных запроса
    var cacheToSave = new CacheObject
    {
        Value = request.Value
    };

    await service.SetAsync(request.Key, cacheToSave, request.SecondsTTL);

    return Results.Created($"/api/cache/{request.Key}", request);
});

// Delete
app.MapDelete("/api/cache/{key}", async (string key, ICacheMethodsService service) => 
{
    var deleted = await service.DeleteAsync(key);
    if(deleted == null) return Results.NotFound();
    return Results.Ok(deleted);
});

// Exists
app.MapGet("/api/cache/exists/{key}", async (string key, ICacheMethodsService service) => 
{
    var exists = await service.ExistsAsync(key);
    return Results.Ok(new { exists });
});

app.MapGet("/api/admin/stats", async (ICacheMethodsService service) => 
{
    if (service is DefaultMethodsService defaultService)
    {
        var stats = await defaultService.GetAdminStatsAsync();
        return Results.Ok(stats);
    }
    return Results.BadRequest();
});

app.Run();
