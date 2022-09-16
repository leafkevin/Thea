using System;

namespace Thea.Orm.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class TableAttribute : Attribute
{
    public string TableName { get; set; }
    public string FieldPrefix { get; set; }
    public TableAttribute(string tableName, string fieldPrefix = null)
    {
        this.TableName = tableName;
        this.FieldPrefix = fieldPrefix;
    }
}