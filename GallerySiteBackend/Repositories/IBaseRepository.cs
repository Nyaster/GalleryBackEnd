namespace GallerySiteBackend.Repositories;

public interface IBaseRepository<T>
{
    public Task<ICollection<T?>> GetAll();
    public Task<T?> GetById(int id);
    public Task Add(T entity);
    public Task Update(T entity);
    public Task Delete(int id);
}