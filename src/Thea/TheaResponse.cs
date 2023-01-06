namespace Thea;

public class TheaResponse<T>
{
    public bool IsSuccess { get; set; } = true;
    public int Code { get; set; }
    public string Message { get; set; }
    public T Data { get; set; }
}

public class TheaResponse : TheaResponse<object>
{
    private static readonly TheaResponse _success = new TheaResponse { IsSuccess = true };
    public static TheaResponse Success => _success;
    public static TheaResponse Succeed(object result = null)
    {
        return new TheaResponse
        {
            IsSuccess = true,
            Data = result
        };
    }
    public static TheaResponse Fail(int code, string message)
    {
        return new TheaResponse
        {
            IsSuccess = false,
            Code = code,
            Message = message
        };
    }
    public static TheaResponse Fail(int code, string message, object data)
    {
        return new TheaResponse
        {
            IsSuccess = false,
            Code = code,
            Message = message,
            Data = data
        };
    }
    public TheaResponse<T> To<T>()
    {
        return new TheaResponse<T>
        {
            IsSuccess = IsSuccess,
            Code = Code,
            Message = Message,
            Data = this.Data.ConvertTo<T>()
        };
    }
}

