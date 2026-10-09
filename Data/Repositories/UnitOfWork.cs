using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Common.Exceptions;

namespace VehicleServiceManagement.Data.Repositories;

/// <summary>
/// Coordinates repositories and commits them as one transaction via <see cref="SaveChangesAsync"/>.
/// Translates optimistic-concurrency failures into user-safe business exceptions.
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;

    /// <summary>
    /// Sets the row version a tracked entity was originally read with. Use when the version came from
    /// an earlier request (for example a hidden form field), so stale edits are detected.
    /// </summary>
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly Dictionary<Type, object> _repositories = new();

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : class
    {
        if (!_repositories.TryGetValue(typeof(T), out var repository))
        {
            repository = new Repository<T>(_context);
            _repositories[typeof(T)] = repository;
        }

        return (IRepository<T>)repository;
    }

    public void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class
        => _context.Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                "This record was changed by another user while you were working on it. Reload the page and try again.");
        }
    }
}
