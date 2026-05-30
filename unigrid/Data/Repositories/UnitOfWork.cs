using System.Threading.Tasks;

namespace unigrid.Data.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly UniGridDbContext _context;

    public UnitOfWork(UniGridDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
}
