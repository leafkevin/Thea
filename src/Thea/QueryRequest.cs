namespace Thea;

public class QueryRequest
{
    public string QueryText { get; set; }
}
public class PagedRequest
{
    private int _pageNumber;
    private int _pageSize;
    public int PageNumber
    {
        get => _pageNumber;
        set
        {
            if (value < 1)
                _pageNumber = 1;
            if (value > 1)
                _pageNumber = value;
            _pageNumber = value;
        }
    }
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value < 0)
                _pageSize = 20;
            if (value > 100)
                _pageSize = 100;
            _pageSize = value;
        }
    }
}

public class QueryPageRequest : PagedRequest
{
    public string QueryText { get; set; }
}
