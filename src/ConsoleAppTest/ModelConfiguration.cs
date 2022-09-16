using Thea.Orm;
using Thea.Trolley;

namespace ConsoleAppTest;

public class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
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
    }
}