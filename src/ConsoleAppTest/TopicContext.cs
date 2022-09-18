using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System;

namespace ConsoleAppTest;
public class TopicContext : DbContext
{
    public DbSet<Topic> Topics { get; set; }
    public DbSet<Category> Categories { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connectionString = "Server=bj-cdb-o9bbr5vl.sql.tencentcdb.com;Port=63227;Database=fengling;Uid=root;password=Siia@TxDb582e4sdf;charset=utf8mb4;";
        optionsBuilder.UseMySql(connectionString, ServerVersion.Create(new Version("5.7"), ServerType.MySql))
            .LogTo(Console.WriteLine);
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //modelBuilder.Entity<Topic>(f =>
        //{
        //    f.ToTable("tts_topic").HasKey(f => f.Id);
        //    f.HasOne(f => f.Category).WithMany(t => t.Topics).HasForeignKey(t => t.CategoryId);
        //});
        modelBuilder.Entity<Category>(f =>
        {
            f.ToTable("tts_category").HasKey(f => f.Id);
            f.HasOne(t => t.Type).WithMany(t => t.Categories).HasForeignKey(t => t.TypeId);
        });
        modelBuilder.Entity<CategoryType>(f =>
        {
            f.ToTable("tts_category_type").HasKey(f => f.Id);
        });
    }
}