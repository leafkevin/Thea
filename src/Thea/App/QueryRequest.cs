namespace Thea;

public class QueryRequest
{
    private int pageSize = 15;
    public string QueryText { get; set; } = string.Empty;
    public int PageIndex { get; set; }
    public int PageSize
    {
        get
        {
            if (this.pageSize > 100) return 100;
            return this.pageSize;
        }
        set
        {
            if (value > 100) this.pageSize = 100;
            else this.pageSize = value;
        }
    }
    public int Offset
    {
        get
        {
            var offset = this.PageIndex;
            if (offset > 0) offset -= 1;
            return offset * this.PageSize;
        }
    }
    public int? TenantId { get; set; }
}
