using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Thea.Cache;

public class RedisCache : IDistributedCache
{
    private readonly string appId;
    private Func<string, int> databaseSelector;
    private readonly ILogger<RedisCache> logger;
    private readonly ConnectionMultiplexer connection;

    public RedisCache(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        this.logger = loggerFactory.CreateLogger<RedisCache>();
        this.appId = configuration.GetValue<string>("AppId");
        var endPoints = configuration.GetSection("Redis:EndPoints").Get<string[]>();
        var password = configuration.GetValue<string>("Redis:Password");
        var timeout = configuration.GetValue("Redis:Timeout", 10000);
        var syncTimeout = configuration.GetValue("Redis:SyncTimeout", 10000);
        var workerCount = configuration.GetValue("Redis:WorkerCount", 500);
        var keepAlive = configuration.GetValue("Redis:KeepAlive", 300);
        var databaseIndex = configuration.GetValue("Redis:Database", 0);
        this.databaseSelector = f => 0;

        var ipEndPoints = new EndPointCollection();
        foreach (var endPoint in endPoints)
        {
            var values = endPoint.Split(":").ToList();
            int port = int.Parse(values[1]);
            if (!IPAddress.TryParse(values[0], out var ipAddress))
            {
                var addresses = Dns.GetHostAddresses(values[0]);
                if (addresses != null)
                {
                    foreach (var address in addresses)
                    {
                        ipEndPoints.Add(new IPEndPoint(address, port));
                    }
                }
            }
            else ipEndPoints.Add(new IPEndPoint(ipAddress, int.Parse(values[1])));
        }

        this.connection = ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = ipEndPoints,
            Password = password,
            ConnectTimeout = timeout,
            SyncTimeout = syncTimeout,
            AbortOnConnectFail = false,
            KeepAlive = keepAlive,
            AllowAdmin = true,
            DefaultDatabase = databaseIndex,
            SocketManager = new SocketManager(this.appId, workerCount, true)
        });
    }
    public void UserDatabase(Func<string, int> databaseSelector)
        => this.databaseSelector = databaseSelector;
    public void Set(string key, object value, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        if (value == null)
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
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

        var database = connection.GetDatabase(this.databaseSelector(key));
        if (lifetimeMinutes == -1)
            await database.StringSetAsync(key, value.ToJson());
        else
        {
            //增加随机秒数，避免缓存雪崩
            var randomSeconds = Random.Shared.Next(-60, 60);
            var expires = TimeSpan.FromMinutes(lifetimeMinutes).Add(TimeSpan.FromSeconds(randomSeconds));
            await database.StringSetAsync(key, value.ToJson(), expires);
        }
    }
    public bool TryGet<T>(string key, out T result)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
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
            if (result == null) return default;
            this.Set(key, result, lifetimeMinutes);
        }
        return result;
    }
    public async Task<(bool, T)> GetAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
        var redisValue = await database.StringGetAsync(key);
        if (redisValue.IsNull) return (false, default);
        return (true, redisValue.ToString().JsonTo<T>());
    }
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> cacheGetter, int lifetimeMinutes = 120)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
        var redisValue = await database.StringGetAsync(key);
        if (redisValue.IsNull)
        {
            var value = await cacheGetter.Invoke();
            if (value == null) return value;
            await this.SetAsync(key, value, lifetimeMinutes);
            return value;
        }
        return redisValue.ToString().JsonTo<T>();
    }
    public async Task<long> IncrementAsync(string key, long initVavlue = 1)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
        var result = await database.StringIncrementAsync(key);
        //设置初始值
        if (initVavlue > result)
            result = await database.StringIncrementAsync(key, initVavlue - result);
        return result;
    }
    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);
        var database = connection.GetDatabase(this.databaseSelector(key));
        database.KeyDelete(key);
    }
    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(key);

        var database = connection.GetDatabase(this.databaseSelector(key));
        await database.KeyDeleteAsync(key);
    }
}
