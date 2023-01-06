namespace Thea.Orm;

public interface IModelConfiguration
{
    void OnModelCreating(ModelBuilder builder);
}
