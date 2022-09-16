namespace Thea.Excel;

public class ExcelRange
{
    public static readonly ExcelRange Empty = new ExcelRange();
    private IExcelCell start;
    private IExcelCell end;

    public IExcelCell Start
    {
        get { return this.start; }
        set
        {
            this.start = value;
            if (this.end != null)
            {
                this.ColumnLetters = $"{this.start.Header.Letter}:{this.end.Header.Letter}";
                this.Range = $"{this.start.Header.Letter}{this.start.RowIndex + 1}:{this.end.Header.Letter}{this.end.RowIndex + 1}";
            }
        }
    }
    public IExcelCell End
    {
        get { return this.end; }
        set
        {
            this.end = value;
            if (this.start != null)
            {
                this.ColumnLetters = $"{this.start.Header.Letter}:{this.end.Header.Letter}";
                this.Range = $"{this.start.Header.Letter}{this.start.RowIndex + 1}:{this.end.Header.Letter}{this.end.RowIndex + 1}";
            }
        }
    }
    public string ColumnLetters { get; private set; }
    public string Range { get; private set; }
    public ExcelCellHorizontalAlignment? HorizontalAlignment { get; set; }
    public ExcelCellVerticalAlignment? VerticalAlignment { get; set; }
    public bool IsEmpty => this.start == null || this.end == null;
    public ExcelRange Merge(ExcelRange range)
    {
        if (this.IsEmpty)
        {
            this.start = range.Start;
            this.end = range.End;
            this.Range = range.Range;
            this.HorizontalAlignment = range.HorizontalAlignment;
            this.VerticalAlignment = range.VerticalAlignment;
            return this;
        }
        if (range.IsEmpty) return this;

        //if ((this.start.RowIndex != range.start.RowIndex && this.start.ColumnIndex != range.start.ColumnIndex)
        //    || (this.end.RowIndex != range.end.RowIndex && this.end.ColumnIndex != range.end.ColumnIndex))
        //    throw new Exception("当前Range无法和现有Range合并");
        if (this.Range == range.Range)
            return this;

        if (range.start.RowIndex < this.start.RowIndex)
            this.start = range.start;
        else if (range.start.ColumnIndex < this.start.ColumnIndex)
            this.start = range.start;

        if (range.end.RowIndex > this.end.RowIndex)
            this.end = range.end;
        else if (range.end.ColumnIndex > this.end.ColumnIndex)
            this.end = range.end;

        this.ColumnLetters = $"{this.start.Header.Letter}:{this.end.Header.Letter}";
        this.Range = $"{this.start.Header.Letter}{this.start.RowIndex + 1}:{this.end.Header.Letter}{this.end.RowIndex + 1}";

        this.start.MergedRange = this;
        this.end.MergedRange = this;
        return this;
    }
    public ExcelRange Merge(IExcelCell cell)
    {
        var range = cell.IsMerged ? cell.MergedRange : cell.AsRange();
        return this.Merge(range);
    }
    public override string ToString() => this.Range;
}
