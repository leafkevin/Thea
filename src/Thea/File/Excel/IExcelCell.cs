namespace Thea.Excel;

public interface IExcelCell
{
    int RowIndex { get; internal set; }
    int ColumnIndex { get; internal set; }
    string CellLetter { get; internal set; }
    ExcelColumnHeader Header { get; internal set; }
    ExcelCellHorizontalAlignment HorizontalAlignment { get; set; }
    ExcelCellVerticalAlignment VerticalAlignment { get; set; }
    object Value { get; }
    bool IsMerged { get; internal set; }
    ExcelRange MergedRange { get; internal set; }
    ExcelRange AsRange();
}
