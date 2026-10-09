using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Maintenance;

public sealed record MaintenanceEntryDto(
    DateTime ServiceDate,
    int Mileage,
    string Description,
    string PartsUsed,
    string Technician,
    decimal LabourCost,
    decimal PartsCost,
    decimal Total);

public sealed record VehicleHistoryDto(
    int VehicleId,
    int CustomerId,
    string RegistrationNumber,
    string VehicleName,
    int CurrentMileage,
    int ServiceIntervalKm,
    int ServiceIntervalMonths,
    int NextServiceMileage,
    DateTime NextServiceDate,
    bool IsDueSoon,
    IReadOnlyList<MaintenanceEntryDto> Entries);

public interface IMaintenanceService
{
    /// <summary>Returns the timeline and next-service estimates for a vehicle.</summary>
    Task<VehicleHistoryDto> GetHistoryAsync(int vehicleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds maintenance timelines. Next-service estimates use the vehicle's interval counted from the last
/// service (or from today when there is no history): whichever of mileage or time comes first is due.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    /// <summary>Warn this many km before the mileage due point.</summary>
    private const int MileageWarningKm = 500;

    /// <summary>Warn this many days before the time due point.</summary>
    private const int TimeWarningDays = 14;

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public MaintenanceService(IUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<VehicleHistoryDto> GetHistoryAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().Query()
            .FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken)
            ?? throw new BusinessRuleException("Vehicle not found.");

        var rows = await _unitOfWork.Repository<MaintenanceHistory>().Query()
            .Where(m => m.VehicleId == vehicleId)
            .OrderBy(m => m.ServiceDate)
            .Select(m => new
            {
                m.ServiceDate,
                m.MileageAtService,
                m.Description,
                m.PartsUsed,
                m.TechnicianName,
                m.LaborCost,
                m.PartsCost
            })
            .ToListAsync(cancellationToken);

        var entries = rows.Select(r => new MaintenanceEntryDto(
            r.ServiceDate, r.MileageAtService, r.Description, r.PartsUsed, r.TechnicianName,
            r.LaborCost, r.PartsCost, r.LaborCost + r.PartsCost)).ToList();

        var now = _timeProvider.GetLocalNow().DateTime;
        var last = entries.LastOrDefault();

        var baseMileage = last?.Mileage ?? vehicle.Mileage;
        var baseDate = last?.ServiceDate ?? now;
        var nextMileage = baseMileage + vehicle.ServiceIntervalKm;
        var nextDate = baseDate.AddMonths(vehicle.ServiceIntervalMonths);

        var isDueSoon = vehicle.Mileage >= nextMileage - MileageWarningKm
            || now >= nextDate.AddDays(-TimeWarningDays);

        return new VehicleHistoryDto(
            vehicle.Id,
            vehicle.CustomerId,
            vehicle.RegistrationNumber,
            $"{vehicle.Year} {vehicle.Make} {vehicle.Model}",
            vehicle.Mileage,
            vehicle.ServiceIntervalKm,
            vehicle.ServiceIntervalMonths,
            nextMileage,
            nextDate,
            isDueSoon,
            entries);
    }
}
