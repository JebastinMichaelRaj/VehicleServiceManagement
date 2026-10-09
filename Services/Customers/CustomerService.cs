using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Customers;

public sealed class CustomerRequest
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public sealed class VehicleRequest
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; } = DateTime.Now.Year;
    public int Mileage { get; set; }
    public int ServiceIntervalKm { get; set; } = 10000;
    public int ServiceIntervalMonths { get; set; } = 6;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record CustomerListItemDto(int Id, string FullName, string Email, string Phone, string State, int VehicleCount);
public sealed record VehicleSummaryDto(int Id, string RegistrationNumber, string Make, string Model, int Year, int Mileage);
public sealed record ServiceHistoryItemDto(int BookingId, string VehicleRegistration, string ServiceName, DateTime ScheduledStart, BookingStatus Status);

public sealed record CustomerDetailsDto(
    int Id,
    string FullName,
    string Email,
    string Phone,
    string State,
    IReadOnlyList<VehicleSummaryDto> Vehicles,
    IReadOnlyList<ServiceHistoryItemDto> ServiceHistory);

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> SearchAsync(string? term, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CustomerDetailsDto?> GetDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<CustomerRequest?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(CustomerRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(CustomerRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<VehicleRequest?> GetVehicleForEditAsync(int vehicleId, CancellationToken cancellationToken = default);
    Task<int> AddVehicleAsync(VehicleRequest request, CancellationToken cancellationToken = default);
    Task UpdateVehicleAsync(VehicleRequest request, CancellationToken cancellationToken = default);
}

public sealed class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;

    public CustomerService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CustomerListItemDto>> SearchAsync(string? term, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = _unitOfWork.Repository<Customer>().Query();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(c => c.FullName.Contains(t) || c.Email.Contains(t) || c.Phone.Contains(t)
                || c.Vehicles.Any(v => v.RegistrationNumber.Contains(t)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItemDto(c.Id, c.FullName, c.Email, c.Phone, c.State, c.Vehicles.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerListItemDto>(items, page, pageSize, total);
    }

    public async Task<CustomerDetailsDto?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<Customer>().Query()
            .Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null) return null;

        var history = await _unitOfWork.Repository<ServiceBooking>().Query()
            .Where(b => b.Vehicle.CustomerId == id)
            .OrderByDescending(b => b.ScheduledStart)
            .Take(100)
            .Select(b => new { b.Id, Registration = b.Vehicle.RegistrationNumber, Service = b.Service.Name, b.ScheduledStart, b.Status })
            .ToListAsync(cancellationToken);

        return new CustomerDetailsDto(
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.State,
            customer.Vehicles
                .OrderBy(v => v.RegistrationNumber)
                .Select(v => new VehicleSummaryDto(v.Id, v.RegistrationNumber, v.Make, v.Model, v.Year, v.Mileage))
                .ToList(),
            history
                .Select(h => new ServiceHistoryItemDto(h.Id, h.Registration, h.Service, h.ScheduledStart, h.Status))
                .ToList());
    }

    public async Task<CustomerRequest?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var c = await _unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        return c is null ? null : new CustomerRequest
        {
            Id = c.Id,
            FullName = c.FullName,
            Email = c.Email,
            Phone = c.Phone,
            State = c.State
        };
    }

    public async Task<int> CreateAsync(CustomerRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureEmailIsUniqueAsync(request.Email, 0, cancellationToken);

        var customer = new Customer
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            State = request.State.Trim()
        };

        await _unitOfWork.Repository<Customer>().AddAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return customer.Id;
    }

    public async Task UpdateAsync(CustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new BusinessRuleException("Customer not found.");

        await EnsureEmailIsUniqueAsync(request.Email, request.Id, cancellationToken);

        customer.FullName = request.FullName.Trim();
        customer.Email = request.Email.Trim();
        customer.Phone = request.Phone.Trim();
        customer.State = request.State.Trim();

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<Customer>().Query()
            .Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new BusinessRuleException("Customer not found.");

        if (customer.Vehicles.Count > 0)
            throw new BusinessRuleException("Remove this customer's vehicles before deleting the customer. Their service records must be kept.");

        _unitOfWork.Repository<Customer>().Remove(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<VehicleRequest?> GetVehicleForEditAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var v = await _unitOfWork.Repository<Vehicle>().GetByIdAsync(vehicleId, cancellationToken);
        return v is null ? null : new VehicleRequest
        {
            Id = v.Id,
            CustomerId = v.CustomerId,
            RegistrationNumber = v.RegistrationNumber,
            Make = v.Make,
            Model = v.Model,
            Year = v.Year,
            Mileage = v.Mileage,
            ServiceIntervalKm = v.ServiceIntervalKm,
            ServiceIntervalMonths = v.ServiceIntervalMonths
        };
    }

    public async Task<int> AddVehicleAsync(VehicleRequest request, CancellationToken cancellationToken = default)
    {
        var customerExists = await _unitOfWork.Repository<Customer>().Query()
            .AnyAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (!customerExists)
            throw new BusinessRuleException("The customer for this vehicle was not found.");

        await EnsureRegistrationIsUniqueAsync(request.RegistrationNumber, 0, cancellationToken);

        var vehicle = new Vehicle
        {
            CustomerId = request.CustomerId,
            RegistrationNumber = request.RegistrationNumber.Trim().ToUpperInvariant(),
            Make = request.Make.Trim(),
            Model = request.Model.Trim(),
            Year = request.Year,
            Mileage = request.Mileage,
            ServiceIntervalKm = request.ServiceIntervalKm,
            ServiceIntervalMonths = request.ServiceIntervalMonths
        };

        await _unitOfWork.Repository<Vehicle>().AddAsync(vehicle, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return vehicle.Id;
    }

    public async Task UpdateVehicleAsync(VehicleRequest request, CancellationToken cancellationToken = default)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new BusinessRuleException("Vehicle not found.");

        if (request.Mileage < vehicle.Mileage)
            throw new BusinessRuleException($"Mileage cannot decrease (recorded: {vehicle.Mileage:N0} km).");

        await EnsureRegistrationIsUniqueAsync(request.RegistrationNumber, request.Id, cancellationToken);

        vehicle.RegistrationNumber = request.RegistrationNumber.Trim().ToUpperInvariant();
        vehicle.Make = request.Make.Trim();
        vehicle.Model = request.Model.Trim();
        vehicle.Year = request.Year;
        vehicle.Mileage = request.Mileage;
        vehicle.ServiceIntervalKm = request.ServiceIntervalKm;
        vehicle.ServiceIntervalMonths = request.ServiceIntervalMonths;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureEmailIsUniqueAsync(string email, int excludingId, CancellationToken ct)
    {
        var normalised = email.Trim();
        if (await _unitOfWork.Repository<Customer>().Query().AnyAsync(c => c.Email == normalised && c.Id != excludingId, ct))
            throw new BusinessRuleException($"Another customer already uses the email '{normalised}'.");
    }

    private async Task EnsureRegistrationIsUniqueAsync(string registration, int excludingId, CancellationToken ct)
    {
        var normalised = registration.Trim().ToUpperInvariant();
        if (await _unitOfWork.Repository<Vehicle>().Query().AnyAsync(v => v.RegistrationNumber == normalised && v.Id != excludingId, ct))
            throw new BusinessRuleException($"A vehicle with registration '{normalised}' already exists.");
    }
}
