namespace Thea;

public class TheaResponse
{
    private static readonly TheaResponse _success = new TheaResponse { IsSuccess = true };
    public bool IsSuccess { get; set; } = true;
    public int Code { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }

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
public class TheaResponse<TResult> : TheaResponse
{
    public new TResult Data { get; set; }
}