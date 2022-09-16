namespace Thea;

public interface IUnitOfWork
{
    void Begin();
    void Commit();
    void Rollback();
}
