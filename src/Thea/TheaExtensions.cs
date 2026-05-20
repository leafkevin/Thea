using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Thea.Json;

namespace Thea;

public static class TheaExtensions
{
    public static T JsonTo<T>(this object obj)
    {
        if (obj == null) return default;
        if (obj is JsonElement element)
            return TheaJsonSerializer.Deserialize<T>(element.GetRawText());
        if (obj is string json)
            return TheaJsonSerializer.Deserialize<T>(json);
        return obj.ConvertTo<T>();
    }
    public static T ConvertTo<T>(this object obj)
    {
        if (obj == null) return default;
        var targetType = typeof(T);
        var type = obj.GetType();
        if (targetType.IsAssignableFrom(type))
            return (T)obj;
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType == null) underlyingType = targetType;
        object result = obj;
        if (underlyingType.IsEnum)
        {
            var enumObj = Convert.ChangeType(result, underlyingType.GetEnumUnderlyingType());
            return (T)Enum.ToObject(underlyingType, enumObj);
        }
        return (T)Convert.ChangeType(result, underlyingType);
    }
    public static string ToJson(this object obj)
    {
        if (obj == null) return null;
        return TheaJsonSerializer.Serialize(obj);
    }
    public static T JsonProperty<T>(this object jsonObj, string propertyName)
    {
        if (jsonObj.TryGetProperty<T>(propertyName, out var value))
            return value;
        return default;
    }
    public static bool TryGetProperty<T>(this object jsonObj, string propertyName, out T value)
    {
        if (jsonObj == null)
        {
            value = default;
            return false;
        }
        if (jsonObj is JsonElement element && element.TryGetProperty(propertyName, out var jsonValue))
        {
            value = jsonValue.JsonTo<T>();
            return true;
        }
        value = default;
        return false;
    }
    public static async Task<T> WaitAsync<T>(this TaskCompletionSource<T> tcs, TimeSpan timeout, string exMessage, CancellationToken cancellationToken = default)
    {
        var cts = new CancellationTokenSource(timeout);
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var message = exMessage ?? $"操作超时, 耗时{timeout.TotalSeconds}s";
        var ctr = cts.Token.Register(() =>
        {
            tcs.TrySetException(new TimeoutException(message));
            if (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                tcs.TrySetException(new TimeoutException(message));
            else tcs.TrySetCanceled(cancellationToken);
        });
        var result = await tcs.Task;
        ctr.Dispose();
        combinedCts.Dispose();
        cts.Dispose();
        return result;
    }
    //public static Task<T> WithTimeout<T>(this TaskCompletionSource<T> tcs, TimeSpan timeout, CancellationToken cancellationToken = default, string exMessage = null)
    //{
    //    var timeoutCts = new CancellationTokenSource(timeout);
    //    var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
    //    var message = exMessage ?? $"操作在{timeout.TotalSeconds}s内未完成";

    //    var registration = combinedCts.Token.Register(() =>
    //    {
    //        if (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
    //            tcs.TrySetException(new TimeoutException(message));
    //        else tcs.TrySetCanceled(cancellationToken);
    //    });
    //    tcs.Task.ContinueWith(_ =>
    //    {
    //        registration.Dispose();
    //        combinedCts.Dispose();
    //        timeoutCts.Dispose();
    //    }, TaskContinuationOptions.ExecuteSynchronously);
    //    return tcs.Task;
    //}
}