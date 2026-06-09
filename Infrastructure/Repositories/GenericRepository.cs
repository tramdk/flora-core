using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Common.Models;
using FloraCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FloraCore.Infrastructure.Repositories;

/// <summary>
/// Generic repository implementation.
/// This class implements the IGenericRepository interface defined in the Application layer.
/// </summary>
public class GenericRepository<TEntity, TKey>(AppDbContext context) : IGenericRepository<TEntity, TKey> where TEntity : class
{
    protected readonly AppDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    protected readonly DbSet<TEntity> _dbSet = context.Set<TEntity>();

    // ========== Basic CRUD Operations ==========

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id)
    {
        return await _dbSet.FindAsync(id);
    }

    /// <inheritdoc />
    [Obsolete("Avoid using GetAllAsync() in production — it loads the entire table. Use GetWithOptionsAsync() with pagination.")]
    public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        return await _dbSet.AsNoTracking().ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity)
    {
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public virtual async Task StageAddAsync(TEntity entity)
    {
        await _dbSet.AddAsync(entity);
        // No SaveChanges — caller must use IUnitOfWork.SaveChangesAsync()
    }

    /// <inheritdoc />
    public virtual async Task StageAddRangeAsync(IEnumerable<TEntity> entities)
    {
        await _dbSet.AddRangeAsync(entities);
        // No SaveChanges — caller must use IUnitOfWork.SaveChangesAsync()
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(TEntity entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public virtual void StageUpdate(TEntity entity)
    {
        _dbSet.Update(entity);
        // No SaveChanges — caller must use IUnitOfWork.SaveChangesAsync()
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TEntity entity)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public virtual void StageDelete(TEntity entity)
    {
        _dbSet.Remove(entity);
        // No SaveChanges — caller must use IUnitOfWork.SaveChangesAsync()
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TKey id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            await DeleteAsync(entity);
        }
    }

    /// <inheritdoc />
    public virtual void StageDelete(TKey id)
    {
        if (_dbSet.Local.Cast<object>().FirstOrDefault(e => {
            var entry = _context.Entry(e);
            var properties = entry.Metadata.FindPrimaryKey()?.Properties;
            var key = properties is { Count: > 0 } ? properties[0] : null;
            return key != null && entry.Property(key.Name).CurrentValue!.Equals(id);
        }) is not TEntity entity)
        {
            entity = Activator.CreateInstance<TEntity>();
            var properties = _context.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.Properties;
            var key = properties is { Count: > 0 } ? properties[0] : null;
            if (key != null)
            {
                _context.Entry(entity).Property(key.Name).CurrentValue = id!;
            }
            _dbSet.Attach(entity);
        }
        _dbSet.Remove(entity);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> GetQueryable()
    {
        return _dbSet.AsQueryable();
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> GetQueryable(QueryOptions<TEntity> options)
    {
        return ApplyQueryOptions(_dbSet.AsQueryable(), options);
    }

    // ========== Advanced Query Operations ==========

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> GetWithOptionsAsync(QueryOptions<TEntity> options)
    {
        var query = ApplyQueryOptions(_dbSet.AsQueryable(), options);
        return await query.ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetSingleWithOptionsAsync(QueryOptions<TEntity> options)
    {
        var query = ApplyQueryOptions(_dbSet.AsQueryable(), options);
        return await query.FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(QueryOptions<TEntity> options)
    {
        var query = _dbSet.AsQueryable();

        // Apply filter
        if (options.Filter != null)
        {
            query = query.Where(options.Filter);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply includes, sorting, and pagination
        query = ApplyQueryOptions(query, options);

        var items = await query.ToListAsync();

        var pageNumber = options.Skip.HasValue && options.Take.HasValue
            ? (options.Skip.Value / options.Take.Value) + 1
            : 1;

        var pageSize = options.Take ?? totalCount;

        return new PagedResult<TEntity>(items, totalCount, pageNumber, pageSize);
    }

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null)
    {
        if (filter == null)
        {
            return await _dbSet.CountAsync();
        }

        return await _dbSet.CountAsync(filter);
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> filter)
    {
        return await _dbSet.AnyAsync(filter);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter)
    {
        return await _dbSet.FirstOrDefaultAsync(filter);
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Apply query options to IQueryable
    /// </summary>
    protected virtual IQueryable<TEntity> ApplyQueryOptions(IQueryable<TEntity> query, QueryOptions<TEntity> options)
    {
        // Apply filter
        if (options.Filter != null)
        {
            query = query.Where(options.Filter);
        }

        // Apply includes
        foreach (var include in options.Includes)
        {
            query = query.Include(include);
        }

        foreach (var includeString in options.IncludeStrings)
        {
            query = query.Include(includeString);
        }

        // Apply sorting
        if (options.OrderBy != null)
        {
            query = query.OrderBy(options.OrderBy);
        }
        else if (options.OrderByDescending != null)
        {
            query = query.OrderByDescending(options.OrderByDescending);
        }

        // Apply no tracking
        if (options.AsNoTracking)
        {
            query = query.AsNoTracking();
        }

        // Apply split query
        if (options.AsSplitQuery)
        {
            query = query.AsSplitQuery();
        }

        // Apply pagination
        if (options.Skip.HasValue)
        {
            query = query.Skip(options.Skip.Value);
        }

        if (options.Take.HasValue)
        {
            query = query.Take(options.Take.Value);
        }

        return query;
    }
}
