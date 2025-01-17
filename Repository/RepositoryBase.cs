﻿using System.Linq.Expressions;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public abstract class RepositoryBase<T>(RepositoryContext repositoryContext) : IRepositoryBase<T>
    where T : class
{
    protected readonly RepositoryContext RepositoryContext = repositoryContext;

    public IQueryable<T> FindAll(bool trackChanges)
    {
        return !trackChanges ? RepositoryContext.Set<T>().AsNoTracking() : RepositoryContext.Set<T>();
    }

    public IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression, bool trackChanges)
    {
        return !trackChanges
            ? RepositoryContext.Set<T>().Where(expression).AsNoTracking()
            : RepositoryContext.Set<T>().Where(expression);
    }

    public async Task Create(T entity)
    {
        await RepositoryContext.Set<T>().AddAsync(entity);
    }

    public void Update(T entity)
    {
        RepositoryContext.Set<T>().Update(entity);
    }

    public void Delete(T entity)
    {
        RepositoryContext.Set<T>().Remove(entity);
    }
}