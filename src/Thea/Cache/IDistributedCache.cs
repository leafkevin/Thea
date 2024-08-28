using System;
using System.Threading.Tasks;

namespace Thea;

public interface IDistributedCache
{
    void Set(string key, object value, int lifetimeMinutes = 120);
    Task SetAsync(string key, object value, int lifetimeMinutes = 120);
    bool TryGet<T>(string key, out T result);
    T Get<T>(string key);
    T GetOrCreate<T>(string key, Func<T> cacheGetter, int lifetimeMinutes = 120);
    Task<(bool, T)> GetAsync<T>(string key);
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> cacheGetter, int lifetimeMinutes = 120);
    Task<long> IncrementAsync(string key, int lifetimeMinutes = 120);
    Task RemoveCache(string key);
}
