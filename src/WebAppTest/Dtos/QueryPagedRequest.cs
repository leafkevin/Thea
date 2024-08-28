namespace WebAppTest.Dtos;

public class PagedRequest
{
    private int _pageNumber;
    private int _pageSize;
    public int PageNumber
    {
        get => this._pageNumber;
        set
        {
            if (value < 1)
                this._pageNumber = 1;
            else this._pageNumber = value;
        }
    }
    public int PageSize
    {
        get => this._pageSize;
        set
        {
            if (value < 0)
                this._pageSize = 20;
            else if (value > 100)
                this._pageSize = 100;
            else this._pageSize = value;
        }
    }
}
public class QueryPagedRequest : PagedRequest
{
    public string QueryText { get; set; }
}
