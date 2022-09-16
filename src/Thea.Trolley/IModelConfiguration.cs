using Thea.Orm;

namespace Thea.Trolley;

public interface IModelConfiguration
{
    void OnModelCreating(ModelBuilder builder);
}
