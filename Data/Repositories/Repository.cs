using Microsoft.EntityFrameworkCore;

namespace VehicleServiceManagement.Data.Repositories;

/// <summary>Generic data access abstraction over a single aggregate/entity set.</summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns a queryable for composing filters, includes and projections.</summary>
    IQueryable<T> Query();

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Remove(T entity);
}

/// <summary>EF Core implementation of <see cref="IRepository{T}"/>.</summary>
public sealed class Repository<T> : IRepository<T> where T : class
{
    private readonly AppDbContext _context;

    public Repository(AppDbContext context)
    {
        _context = context;
    }

    private DbSet<T> Set => _context.Set<T>();

    public async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await Set.FindAsync([id], cancellationToken);

    public IQueryable<T> Query() => Set.AsQueryable();

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken);

    public void Remove(T entity) => Set.Remove(entity);
}
