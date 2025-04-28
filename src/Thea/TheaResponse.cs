namespace Thea;

public class TheaResponse<TResult>
{
    public bool IsSuccess { get; set; }
    public int Code { get; set; }
    public string Message { get; set; }
    public TResult Data { get; set; }
}
public class TheaResponse : TheaResponse<object>
{
    private static readonly TheaResponse _success = new TheaResponse { IsSuccess = true };
    public static TheaResponse Success => _success;
    public static TheaResponse Succeed(object result, int code = 0, string message = null)
    {
        return new TheaResponse
        {
            IsSuccess = true,
            Code = code,
            Data = result,
            Message = message
        };
    }
    public static TheaResponse Fail(int code, string message = null, object data = null)
    {
        return new TheaResponse
        {
            IsSuccess = false,
            Code = code,
            Message = message,
            Data = data
        };
    }
}
