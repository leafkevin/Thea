namespace Thea;

public class TheaResponse
{
    private static readonly TheaResponse _success = new TheaResponse { IsSuccess = true };
    public static TheaResponse Success => _success;

    public bool IsSuccess { get; set; } = true;
    public int Code { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }

    public static TheaResponse<TResult> Succeed<TResult>(TResult result, int code = 0, string message = null)
    {
        return new TheaResponse<TResult>
        {
            IsSuccess = true,
            Code = code,
            Data = result,
            Message = message
        };
    }
    public static TheaResponse Fail(int code, string message = null, object data = default)
    {
        return new TheaResponse
        {
            IsSuccess = false,
            Code = code,
            Message = message,
            Data = data
        };
    }
    public static TheaResponse<TResult> Fail<TResult>(int code, string message = null, TResult data = default)
    {
        return new TheaResponse<TResult>
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
    public new TResult Data { get => (TResult)base.Data; set => base.Data = value; }
    public TheaResponse() : base()
    {
        this.Data = (TResult)base.Data;
    }
}