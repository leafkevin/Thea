using System.Collections.Generic;

namespace Thea.Excel;

public interface IExcelRow
{
    int RowIndex { get; set; }
    List<IExcelCell> Cells { get; set; }
    IExcelCell this[string field] { get; }
    object RowData { get; set; }
    bool TryGetCell(string field, out IExcelCell cell);
    ExcelRange Range(int startCellIndex, int endCellIndex);
    ExcelRange Range(IExcelCell start, IExcelCell end);
}
