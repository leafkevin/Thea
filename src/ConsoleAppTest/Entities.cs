using System;
using System.Collections.Generic;

namespace ConsoleAppTest;


public class Topic
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int? Clicks { get; set; }
    public DateTime CreatedAt { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; }
}
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }

    public int TypeId { get; set; }
    public CategoryType Type { get; set; }
    public List<Topic> Topics { get; set; }
}
public class CategoryType
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Category> Categories { get; set; }
}