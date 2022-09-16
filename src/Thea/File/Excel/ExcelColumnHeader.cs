using System;

namespace Thea.Excel;

public class ExcelColumnHeader
{
    public string Field { get; set; }
    public string Title { get; set; }
    public string Letter { get; set; }
    public string Format { get; set; }
    public int? ColumnWidth { get; set; }
    public ExcelCellHorizontalAlignment? HorizontalAlignment { get; set; }
    public ExcelCellVerticalAlignment? VerticalAlignment { get; set; }
    public Func<object, object> ValueDecorator { get; set; }
    public override string ToString() => this.Letter;
}
