using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Services.Inventory;

namespace VehicleServiceManagement.Services.Booking;

/// <summary>Input for scheduling a new service appointment.</summary>
public sealed class CreateBookingRequest
{
    public int VehicleId { get; set; }
    public int ServiceId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public string TechnicianName { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>Input for closing out a booking: odometer reading, parts consumed, and discount.</summary>
public sealed class CompleteBookingRequest
{
    public int Mileage { get; set; }
    public decimal DiscountPercent { get; set; }
    public List<PartUsageRequest> Parts { get; set; } = new();
}

/// <summary>Read model for booking lists.</summary>
public sealed class BookingListItemDto
{
    public int Id { get; init; }
    public string VehicleRegistration { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string TechnicianName { get; init; } = string.Empty;
    public DateTime ScheduledStart { get; init; }
    public DateTime ScheduledEnd { get; init; }
    public BookingStatus Status { get; init; }
}

public sealed record LookupItem(int Id, string Label);

public sealed record BookingLookups(IReadOnlyList<LookupItem> Vehicles, IReadOnlyList<LookupItem> Services);
