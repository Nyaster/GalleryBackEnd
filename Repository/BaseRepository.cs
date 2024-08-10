using Microsoft.EntityFrameworkCore;

namespace Repository;

public abstract class BaseRepository<T> : IBaseRepository<T> where T : class
{
    protected readonly RepositoryContext _context;
    protected readonly DbSet<T> _dbSet;

    public BaseRepository(RepositoryContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    public async Task<ICollection<T?>> GetAll()
    {
        return (await _dbSet.ToListAsync())!;
    }

    public async Task<T?> GetById(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task Add(T entity)
    {
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
    }

    public async Task Update(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task Delete(int id)
    {
        var entity = await GetById(id);
        if (entity != null)
        {
            _dbSet.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}