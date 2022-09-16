// See https://aka.ms/new-console-template for more information
using ConsoleAppTest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using ServiceStack;
using ServiceStack.OrmLite;
using System;
using System.Linq;
using System.Linq.Expressions;
using Thea.Orm;
using Thea.Trolley;
using Thea.Trolley.Providers;

var services = new ServiceCollection();
services.AddSingleton<IOrmProvider, MySqlProvider>();
services.AddSingleton<IOrmDbFactory, OrmDbFactory>(f =>
{
    var dbFactory = new OrmDbFactory(f);
    var builder = dbFactory.CreateModelBuidler();
    builder.Entity<Topic>(f =>
    {
        f.ToTable("tts_topic").Key(f => f.Id).AutoIncrement(f => f.Id);
        f.Member(f => f.Category).Navigate(nameof(Topic.CategoryId));
    });
    builder.Entity<Category>(f =>
    {
        f.ToTable("tts_category").Key(f => f.Id).AutoIncrement(f => f.Id);
        f.Member(f => f.Topics).Navigate(nameof(Topic.CategoryId));
    });
    return dbFactory;
});

//var connectionString = "Server=bj-cdb-o9bbr5vl.sql.tencentcdb.com;Port=63227;Database=fengling;Uid=root;password=Siia@TxDb582e4sdf;charset=utf8mb4;";
//services.AddDbContext<TopicContext>(options =>
//    options.UseMySql(connectionString, ServerVersion.Create(new Version("5.7"), ServerType.MySql))
//    .LogTo(Console.WriteLine));

var serviceProvider = services.BuildServiceProvider();


//var fsql = new FreeSql.FreeSqlBuilder()
//   .UseConnectionString(FreeSql.DataType.MySql, connectionString)
//   //.UseAutoSyncStructure(true)
//   //.UseLazyLoading(true)
//   .Build();

//fsql.Update<Topic>().Set(f => new { f.Name, f.Clicks }, new { Name = "333", Clicks = 33 })
//    .Where(f => f.Id == 4).ExecuteAffrows();


//using (var db = new TopicContext())
//{
//    var blogs = db.Topics
//        .Where(b => b.Clicks > 30)
//        .OrderBy(b => b.Id)
//        .Include(f => f.Category)
//        .Include(f => f.Category.Type)
//        .ToList();
//    int ddd = 0;
//}

//var list = fsql.Select<Topic>()
//    .GroupBy(a => new { tt2 = a.Name.Substring(0, 2), mod4 = a.Id % 4 })
//    .Having(a => a.Count() > 0 && a.Avg(a.Key.mod4) > 0 && a.Max(a.Key.mod4) > 0)
//    .Having(a => a.Count() < 300 || a.Avg(a.Key.mod4) < 100)
//    .OrderBy(a => a.Key.tt2)
//    .OrderByDescending(a => a.Count())
//    .ToList(a => new { a.Key, cou1 = a.Count(), arg1 = a.Avg(a.Value.Clicks) });

//var sql = fsql.Select<Category>()
//  //.InnerJoin(f => f == f.Category.Id)
//  .Include(f => f.Topics)
//  //.Where(f => f.Id > 0)
//  .ToSql((a, b) => new { a.Id, CategoryName = a.Name, TopicName = b.Name });

//var dbFactory = new OrmLiteConnectionFactory(connectionString, MySqlConnectorDialect.Provider);
//using var db = dbFactory.Open();

//var explicitJoin = db.Select(db.From<Topic>()
//    .Join<Category>((a, b) => a.CategoryId == b.Id)
//    .Join<CategoryType>((a, b) => a.Category.TypeId == b.Id)
//    .Where<Category>(x => x.Name == "Nirvana")
//     );

//VisitExpr(f => new int[] { 1, 2, 3 }.Contains(f.IntColumn) || ((f.StringColumn == "123" && f.IntColumn > 10) || f.BoolColumn) && f.BoolNullableColumn.HasValue);
//VisitExpr(f => f.StringColumn.Contains("123"));
//VisitExpr(f => new { f.BoolColumn, f.IntColumn });

string abc = "345";
string cp = "abc-123";
int[] iArray = new int[] { 1, 2, 3 };
string[] strArray = new string[] { "123", "456" };

VisitExpr(f => new { Constant1 = abc, Constant2 = "123" + "789", f.Name, TopicId = f.Id, CategoryName = f.Category.Name + "-" + f.Name }, "578");
Console.WriteLine("Hello, World!");



void VisitExpr<TTarget>(Expression<Func<Topic, TTarget>> selectExpr, string strParameter)
{
    var ormProvider = serviceProvider.GetService<IOrmProvider>();
    var dbFactory = serviceProvider.GetService<IOrmDbFactory>();
    var exprVisitor = new Thea.Trolley.SqlExpressionVisitor(dbFactory, ormProvider);
    Expression<Func<Topic, bool>> whereExpr = f => iArray.Contains(f.Id) && strArray.Contains(abc) && f.Category.Name.Contains(cp) && f.Name == strParameter && f.Category.IsEnabled && f.Clicks.HasValue;
    var sql = exprVisitor.From(typeof(Topic)).Where(whereExpr).Select(selectExpr).BuildSql();
    //((`StringColumn`= '123' AND `IntColumn`> 10) OR `BoolColumn`= 1) AND (`BoolNullableColumn` is not null)
    int sdfds = 0;
}
