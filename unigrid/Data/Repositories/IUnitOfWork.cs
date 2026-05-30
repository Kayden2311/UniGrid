using System.Threading.Tasks;

namespace unigrid.Data.Repositories;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync();
}
