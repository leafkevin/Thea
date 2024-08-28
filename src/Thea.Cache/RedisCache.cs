using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Thea.Cache;

public class RedisCache : IDistributedCache
{
    private readonly int databaseIndex = 0;
    private readonly string url;
    private readonly ConnectionMultiplexer connectionPool;

    public RedisCache(IConfiguration configuration)
    {
        this.url = configuration.GetValue<string>("Redis:Url");
        if (string.IsNullOrEmpty(this.url))
            throw new ArgumentNullException("缺少配置Redis:Url");
        this.databaseIndex = configuration.GetValue("Redis:Database", -1);
        this.connectionPool = ConnectionMultiplexer.Connect(url);
    }
    public void Set(string key, object value, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        if (value == null)
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        var randomSeconds = Random.Shared.Next(-60, 60);
        var expires = TimeSpan.FromMinutes(lifetimeMinutes).Add(TimeSpan.FromSeconds(randomSeconds));
        database.StringSet(key, value.ToJson(), expires);
    }
    public async Task SetAsync(string key, object value, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        if (value == null)
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        if (lifetimeMinutes == -1)
            await database.StringSetAsync(key, value.ToJson());
        else
        {
            var randomSeconds = Random.Shared.Next(-60, 60);
            var expires = TimeSpan.FromMinutes(lifetimeMinutes).Add(TimeSpan.FromSeconds(randomSeconds));
            await database.StringSetAsync(key, value.ToJson(), expires);
        }
    }
    public bool TryGet<T>(string key, out T result)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        var redisValue = database.StringGet(key);
        if (redisValue.IsNull)
        {
            result = default;
            return false;
        }
        result = redisValue.ToString().JsonTo<T>();
        return true;
    }
    public T Get<T>(string key)
    {
        this.TryGet<T>(key, out var result);
        return result;
    }
    public T GetOrCreate<T>(string key, Func<T> cacheGetter, int lifetimeMinutes = 120)
    {
        if (!this.TryGet<T>(key, out var result))
        {
            result = cacheGetter.Invoke();
            this.Set(key, result, lifetimeMinutes);
        }
        return result;
    }
    public async Task<(bool, T)> GetAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        var redisValue = await database.StringGetAsync(key);
        if (redisValue.IsNull) return (false, default);
        return (true, redisValue.ToString().JsonTo<T>());
    }
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> cacheGetter, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        var redisValue = await database.StringGetAsync(key);
        if (redisValue.IsNull)
        {
            var value = await cacheGetter.Invoke();
            await this.SetAsync(key, value, lifetimeMinutes);
            return value;
        }
        return redisValue.ToString().JsonTo<T>();
    }
    public async Task<long> IncrementAsync(string key, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        if (!database.KeyExists(key))
        {
            var randomSeconds = Random.Shared.Next(-60, 60);
            var expires = TimeSpan.FromMinutes(lifetimeMinutes).Add(TimeSpan.FromSeconds(randomSeconds));
            database.StringSet(key, 0, expires);
        }
        var result = await database.StringIncrementAsync(key);
        return result;
    }
    public async Task RemoveCache(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connectionPool.GetDatabase(this.databaseIndex);
        await database.KeyDeleteAsync(key);
    }
}
