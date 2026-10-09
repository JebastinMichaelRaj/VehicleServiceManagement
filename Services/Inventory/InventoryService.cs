using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Inventory;

public sealed class PartRequest
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public int QuantityOnHand { get; set; }
    public int ReorderLevel { get; set; }
    /// <summary>Row version read when the edit form was loaded (base64). Detects edits made in between.</summary>
    public string? RowVersionBase64 { get; set; }
}

/// <summary>A part line on a job, as submitted by the user.</summary>
public sealed class PartUsageRequest
{
    public int PartId { get; set; }
    public int Quantity { get; set; }
}

public sealed record PartListItemDto(int Id, string Sku, string Name, decimal UnitPrice, int QuantityOnHand, int ReorderLevel, bool IsLowStock);

public interface IInventoryService
{
    Task<IReadOnlyList<PartListItemDto>> ListAsync(string? search, bool lowStockOnly, CancellationToken cancellationToken = default);
    Task<PartRequest?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(PartRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(PartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates stock for every line, decrements quantities and returns tracked <see cref="JobPart"/> rows
    /// priced at current list price. Does not save; the caller commits with the same unit of work.
    /// </summary>
    Task<IReadOnlyList<JobPart>> ReserveForJobAsync(int bookingId, IEnumerable<PartUsageRequest> lines, CancellationToken cancellationToken = default);

    Task<int> CountLowStockAsync(CancellationToken cancellationToken = default);
}

public sealed class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(IUnitOfWork unitOfWork, ILogger<InventoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PartListItemDto>> ListAsync(string? search, bool lowStockOnly, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Part>().Query();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Sku.Contains(term));
        }

        if (lowStockOnly)
            query = query.Where(p => p.QuantityOnHand <= p.ReorderLevel);

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new PartListItemDto(p.Id, p.Sku, p.Name, p.UnitPrice, p.QuantityOnHand, p.ReorderLevel,
                p.QuantityOnHand <= p.ReorderLevel))
            .ToListAsync(cancellationToken);
    }

    public async Task<PartRequest?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var part = await _unitOfWork.Repository<Part>().GetByIdAsync(id, cancellationToken);
        if (part is null) return null;

        return new PartRequest
        {
            Id = part.Id,
            Sku = part.Sku,
            Name = part.Name,
            UnitCost = part.UnitCost,
            UnitPrice = part.UnitPrice,
            QuantityOnHand = part.QuantityOnHand,
            ReorderLevel = part.ReorderLevel,
            RowVersionBase64 = Convert.ToBase64String(part.RowVersion)
        };
    }

    public async Task<int> CreateAsync(PartRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureSkuIsUniqueAsync(request.Sku.Trim(), 0, cancellationToken);

        var part = new Part
        {
            Sku = request.Sku.Trim(),
            Name = request.Name.Trim(),
            UnitCost = request.UnitCost,
            UnitPrice = request.UnitPrice,
            QuantityOnHand = request.QuantityOnHand,
            ReorderLevel = request.ReorderLevel
        };

        await _unitOfWork.Repository<Part>().AddAsync(part, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return part.Id;
    }

    public async Task UpdateAsync(PartRequest request, CancellationToken cancellationToken = default)
    {
        var part = await _unitOfWork.Repository<Part>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new BusinessRuleException("The part no longer exists.");

        await EnsureSkuIsUniqueAsync(request.Sku.Trim(), request.Id, cancellationToken);

        if (!string.IsNullOrEmpty(request.RowVersionBase64))
            _unitOfWork.SetOriginalRowVersion(part, Convert.FromBase64String(request.RowVersionBase64));

        part.Sku = request.Sku.Trim();
        part.Name = request.Name.Trim();
        part.UnitCost = request.UnitCost;
        part.UnitPrice = request.UnitPrice;
        part.QuantityOnHand = request.QuantityOnHand;
        part.ReorderLevel = request.ReorderLevel;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobPart>> ReserveForJobAsync(int bookingId, IEnumerable<PartUsageRequest> lines, CancellationToken cancellationToken = default)
    {
        var reserved = new List<JobPart>();

        // Empty rows (no part selected or zero quantity) are ignored so unused form rows are harmless.
        var effectiveLines = lines.Where(l => l.PartId > 0 && l.Quantity > 0)
            .GroupBy(l => l.PartId)
            .Select(g => new PartUsageRequest { PartId = g.Key, Quantity = g.Sum(x => x.Quantity) });

        foreach (var line in effectiveLines)
        {
            var part = await _unitOfWork.Repository<Part>().GetByIdAsync(line.PartId, cancellationToken)
                ?? throw new BusinessRuleException($"Part {line.PartId} was not found.");

            if (part.QuantityOnHand < line.Quantity)
                throw new BusinessRuleException(
                    $"Insufficient stock for {part.Name}: {part.QuantityOnHand} available, {line.Quantity} requested.");

            part.QuantityOnHand -= line.Quantity;

            if (part.QuantityOnHand <= part.ReorderLevel)
                _logger.LogWarning("Part {PartId} ({PartName}) is at or below reorder level ({Quantity} left)",
                    part.Id, part.Name, part.QuantityOnHand);

            var jobPart = new JobPart
            {
                ServiceBookingId = bookingId,
                PartId = part.Id,
                Part = part,
                Quantity = line.Quantity,
                UnitPrice = part.UnitPrice,
                UnitCost = part.UnitCost
            };

            await _unitOfWork.Repository<JobPart>().AddAsync(jobPart, cancellationToken);
            reserved.Add(jobPart);
        }

        return reserved;
    }

    public Task<int> CountLowStockAsync(CancellationToken cancellationToken = default)
        => _unitOfWork.Repository<Part>().Query()
            .CountAsync(p => p.QuantityOnHand <= p.ReorderLevel, cancellationToken);

    private async Task EnsureSkuIsUniqueAsync(string sku, int excludingId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Repository<Part>().Query()
            .AnyAsync(p => p.Sku == sku && p.Id != excludingId, cancellationToken);

        if (exists)
            throw new BusinessRuleException($"A part with SKU '{sku}' already exists.");
    }
}
