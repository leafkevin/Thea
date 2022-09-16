using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Thea.Excel;

public class ExcelExportInfo
{
    public string SheetName { get; set; } = "Sheet1";
    public bool IsAllowMerge { get; set; }
    public double DefaultRowHeight { get; set; } = 15d;
    public double DefaultColumnWidth { get; set; } = 11.5d;
    public ExcelCellHorizontalAlignment DefaultHorizontalAlignment { get; set; } = ExcelCellHorizontalAlignment.General;
    public ExcelCellVerticalAlignment DefaultVerticalAlignment { get; set; } = ExcelCellVerticalAlignment.Center;
    public List<ExcelColumnHeader> Headers { get; set; }
    public CellMergePredicate CellMergePredicate { get; set; }

    public static ExcelExportInfo From<TEntity>(Action<ExcelExportInfoBuilder<TEntity>> initializer)
    {
        if (initializer == null) throw new ArgumentNullException(nameof(initializer));

        var builder = new ExcelExportInfoBuilder<TEntity>();
        initializer.Invoke(builder);
        return builder.Build();
    }
}
public class ExcelExportInfoBuilder<TEntity>
{
    private readonly ExcelExportInfo exportInfo = new ExcelExportInfo { Headers = new List<ExcelColumnHeader>() };
    public ExcelExportInfoBuilder<TEntity> AddColumnHeader(Action<ExcelColumnHeaderBuilder<TEntity>> initializer)
    {
        if (initializer == null) throw new ArgumentNullException(nameof(initializer));

        var headerBuilder = new ExcelColumnHeaderBuilder<TEntity>();
        initializer.Invoke(headerBuilder);
        exportInfo.Headers.Add(headerBuilder.Build());
        return this;
    }
    public ExcelExportInfoBuilder<TEntity> Create(string sheetName = "Sheet1", double defaultRowHeight = 15d, double defaultColumnWidth = 11.5d)
    {
        this.exportInfo.SheetName = sheetName;
        this.exportInfo.DefaultRowHeight = defaultRowHeight;
        this.exportInfo.DefaultColumnWidth = defaultColumnWidth;
        return this;
    }
    public ExcelExportInfo Build()
    {
        if (exportInfo.Headers.Count <= 0)
            throw new Exception("请配置导出列:Headers");
        return this.exportInfo;
    }
}
public class ExcelColumnHeaderBuilder<TEntity>
{
    private ExcelColumnHeader header = new ExcelColumnHeader();
    public ExcelColumnHeaderBuilder<TEntity> Field<TField>(Expression<Func<TEntity, TField>> fieldExpr)
    {
        var memberExpr = fieldExpr as MemberExpression;
        this.header.Field = memberExpr.Member.Name;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Title(string title)
    {
        this.header.Title = title;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Format(string format)
    {
        this.header.Format = format;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Create<TField>(Expression<Func<TEntity, TField>> fieldExpr, string title, string format = null)
        => this.Field<TField>(fieldExpr).Title(title).Format(format);
    public ExcelColumnHeaderBuilder<TEntity> Horizontal(ExcelCellHorizontalAlignment alignment)
    {
        this.header.HorizontalAlignment = alignment;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Vertical(ExcelCellVerticalAlignment alignment)
    {
        this.header.VerticalAlignment = alignment;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Width(int width)
    {
        this.header.ColumnWidth = width;
        return this;
    }
    public ExcelColumnHeaderBuilder<TEntity> Decorator(Func<object, object> valueDecorator)
    {
        this.header.ValueDecorator = valueDecorator;
        return this;
    }
    public ExcelColumnHeader Build()
    {
        if (string.IsNullOrEmpty(this.header.Title))
            this.header.Title = this.header.Field;
        return this.header;
    }
}
