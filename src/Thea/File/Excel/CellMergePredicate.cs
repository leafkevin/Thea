using System.Collections.Generic;

namespace Thea.Excel;

public delegate bool CellMergePredicate(IExcelRow curRow, IExcelRow lastRow, out List<ExcelRange> ranges);
