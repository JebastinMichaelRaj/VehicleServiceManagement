using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Services.Inventory;
using VehicleServiceManagement.Services.Invoicing;
using VehicleServiceManagement.Services.Notifications;

namespace VehicleServiceManagement.Services.Booking;

public interface IBookingService
{
    Task<int> ScheduleAsync(CreateBookingRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(int bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a job in one transaction: reserves stock, records maintenance history,
    /// updates mileage and issues the invoice.
    /// </summary>
    Task CompleteAsync(int bookingId, CompleteBookingRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingListItemDto>> GetUpcomingAsync(DateTime from, int take, CancellationToken cancellationToken = default);
    Task<BookingLookups> GetLookupsAsync(CancellationToken cancellationToken = default);
}

public sealed class BookingService : IBookingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBookingPolicy _policy;
    private readonly IInvoiceService _invoiceService;
    private readonly IInventoryService _inventory;
    private readonly INotificationSender _notifications;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IUnitOfWork unitOfWork,
        IBookingPolicy policy,
        IInvoiceService invoiceService,
        IInventoryService inventory,
        INotificationSender notifications,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<BookingService> logger)
    {
        _unitOfWork = unitOfWork;
        _policy = policy;
        _invoiceService = invoiceService;
        _inventory = inventory;
        _notifications = notifications;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ScheduleAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        var vehicle = await _unitOfWork.Repository<Vehicle>().Query()
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken)
            ?? throw new BusinessRuleException($"Vehicle {request.VehicleId} was not found.");

        var service = await _unitOfWork.Repository<Service>().GetByIdAsync(request.ServiceId, cancellationToken)
            ?? throw new BusinessRuleException($"Service {request.ServiceId} was not found.");

        var start = request.ScheduledStart;
        var end = start.AddMinutes((double)(service.EstimatedHours * 60m));

        _policy.Validate(start, service.EstimatedHours, _timeProvider.GetLocalNow().DateTime);

        var technician = request.TechnicianName.Trim();
        var hasConflict = await _unitOfWork.Repository<ServiceBooking>().Query().AnyAsync(b =>
            b.Status != BookingStatus.Cancelled &&
            b.ScheduledStart < end && start < b.ScheduledEnd &&
            (b.TechnicianName == technician || b.VehicleId == vehicle.Id),
            cancellationToken);

        if (hasConflict)
            throw new BusinessRuleException(
                "The selected technician or vehicle already has an overlapping appointment in this time window.");

        var booking = new ServiceBooking
        {
            VehicleId = vehicle.Id,
            ServiceId = service.Id,
            ScheduledStart = start,
            ScheduledEnd = end,
            DurationHours = service.EstimatedHours,
            TechnicianName = technician,
            Notes = request.Notes?.Trim(),
            Status = BookingStatus.Scheduled
        };

        await _unitOfWork.Repository<ServiceBooking>().AddAsync(booking, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Booking {BookingId} scheduled for vehicle {VehicleId} at {Start}", booking.Id, vehicle.Id, start);

        await NotifySafelyAsync(vehicle.Customer,
            "Appointment confirmed",
            $"{service.Name} for {vehicle.RegistrationNumber} on {start:ddd dd MMM, HH:mm} with {technician}.",
            cancellationToken);

        return booking.Id;
    }

    public async Task CancelAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _unitOfWork.Repository<ServiceBooking>().GetByIdAsync(bookingId, cancellationToken)
            ?? throw new BusinessRuleException($"Booking {bookingId} was not found.");

        if (booking.Status is BookingStatus.Completed)
            throw new BusinessRuleException("Completed bookings cannot be cancelled.");

        booking.Status = BookingStatus.Cancelled;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Booking {BookingId} cancelled", bookingId);
    }

    public async Task CompleteAsync(int bookingId, CompleteBookingRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await _unitOfWork.Repository<ServiceBooking>().Query()
            .Include(b => b.Service)
            .Include(b => b.Vehicle).ThenInclude(v => v.Customer)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new BusinessRuleException($"Booking {bookingId} was not found.");

        if (booking.Status is BookingStatus.Completed or BookingStatus.Cancelled)
            throw new BusinessRuleException($"A {booking.Status.ToString().ToLowerInvariant()} booking cannot be completed.");

        if (request.Mileage < booking.Vehicle.Mileage)
            throw new BusinessRuleException(
                $"Mileage ({request.Mileage:N0}) cannot be lower than the recorded mileage ({booking.Vehicle.Mileage:N0}).");

        // Stock is reserved first; any shortage throws before anything is marked complete.
        var jobParts = await _inventory.ReserveForJobAsync(booking.Id, request.Parts, cancellationToken);
        var partsCost = jobParts.Sum(p => p.UnitPrice * p.Quantity);
        var partsDescription = string.Join(", ", jobParts.Select(p => $"{p.Part.Name} x{p.Quantity}"));

        var now = _timeProvider.GetLocalNow().DateTime;

        booking.Status = BookingStatus.Completed;
        booking.Vehicle.Mileage = request.Mileage;

        await _unitOfWork.Repository<MaintenanceHistory>().AddAsync(new MaintenanceHistory
        {
            VehicleId = booking.VehicleId,
            ServiceBookingId = booking.Id,
            ServiceDate = now,
            MileageAtService = request.Mileage,
            Description = booking.Service.Name,
            PartsUsed = partsDescription,
            LaborHours = booking.DurationHours,
            LaborCost = booking.Service.BasePrice,
            PartsCost = partsCost,
            TechnicianName = booking.TechnicianName
        }, cancellationToken);

        await _invoiceService.CreateForCompletedBookingAsync(
            booking, partsCost, request.DiscountPercent, booking.Vehicle.Customer.State, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Booking {BookingId} completed; invoice issued", bookingId);
    }

    public async Task<IReadOnlyList<BookingListItemDto>> GetUpcomingAsync(DateTime from, int take, CancellationToken cancellationToken = default)
        => await _unitOfWork.Repository<ServiceBooking>().Query()
            .Where(b => b.ScheduledStart >= from && b.Status != BookingStatus.Cancelled)
            .OrderBy(b => b.ScheduledStart)
            .Take(take)
            .ProjectTo<BookingListItemDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);

    public async Task<BookingLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var vehicles = await _unitOfWork.Repository<Vehicle>().Query()
            .OrderBy(v => v.RegistrationNumber)
            .Select(v => new LookupItem(v.Id, $"{v.RegistrationNumber} - {v.Make} {v.Model}"))
            .ToListAsync(cancellationToken);

        var services = await _unitOfWork.Repository<Service>().Query()
            .OrderBy(s => s.Name)
            .Select(s => new LookupItem(s.Id, $"{s.Name} ({s.BasePrice:N0})"))
            .ToListAsync(cancellationToken);

        return new BookingLookups(vehicles, services);
    }

    private async Task NotifySafelyAsync(Customer customer, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.SendAsync(new NotificationMessage(customer.Email, customer.Phone, subject, body), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A notification failure must not undo a committed booking.
            _logger.LogWarning(ex, "Notification '{Subject}' failed for customer {CustomerId}", subject, customer.Id);
        }
    }
}
