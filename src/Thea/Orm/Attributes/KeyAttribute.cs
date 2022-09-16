using System;

namespace Thea.Orm.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class KeyAttribute : Attribute
{
    /// <summary>
    /// 数据库字段名称
    /// </summary>
    public string FieldName { get; set; }
    /// <summary>
    /// 数据库字段类型，如：typeof(stirng),typeof(DateTime)
    /// </summary>
    public Type FieldType { get; set; }
    /// <summary>
    /// 是否自增长字段
    /// </summary>
    public bool IsAutoIncrement { get; set; } = false;
    public KeyAttribute() { }
    public KeyAttribute(string fieldName) => this.FieldName = fieldName;
    public KeyAttribute(Type fieldType) => this.FieldType = fieldType;
    public KeyAttribute(bool isAutoIncrement) => this.IsAutoIncrement = isAutoIncrement;
    public KeyAttribute(string fieldName, Type fieldType)
    {
        this.FieldName = fieldName;
        this.FieldType = fieldType;
    }
    public KeyAttribute(bool isAutoIncrement, string fieldName)
    {
        this.IsAutoIncrement = isAutoIncrement;
        this.FieldName = fieldName;
    }
    public KeyAttribute(bool isAutoIncrement, Type fieldType)
    {
        this.IsAutoIncrement = isAutoIncrement;
        this.FieldType = fieldType;
    }
    public KeyAttribute(bool isAutoIncrement, string fieldName, Type fieldType)
    {
        this.IsAutoIncrement = isAutoIncrement;
        this.FieldName = fieldName;
        this.FieldType = fieldType;
    }
}
