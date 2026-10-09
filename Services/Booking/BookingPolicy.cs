using Microsoft.Extensions.Options;
using VehicleServiceManagement.Common.Exceptions;

namespace VehicleServiceManagement.Services.Booking;

/// <summary>Validates whether a time slot is acceptable under workshop rules (independent of existing bookings).</summary>
public interface IBookingPolicy
{
    /// <exception cref="BusinessRuleException">Thrown when the slot violates a scheduling rule.</exception>
    void Validate(DateTime start, decimal durationHours, DateTime now);
}

public sealed class BookingPolicy : IBookingPolicy
{
    private readonly SchedulingOptions _options;

    public BookingPolicy(IOptions<SchedulingOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public void Validate(DateTime start, decimal durationHours, DateTime now)
    {
        if (durationHours <= 0)
            throw new BusinessRuleException("Service duration must be greater than zero.");

        if (start < now.AddHours(_options.MinimumLeadTimeHours))
            throw new BusinessRuleException($"Bookings must be made at least {_options.MinimumLeadTimeHours} hour(s) in advance.");

        if (start.DayOfWeek == DayOfWeek.Sunday)
            throw new BusinessRuleException("The workshop is closed on Sundays.");

        var end = start.AddMinutes((double)(durationHours * 60m));
        var opening = start.Date.AddHours(_options.OpeningHour);
        var closing = start.Date.AddHours(_options.ClosingHour);

        if (start < opening || end > closing)
            throw new BusinessRuleException(
                $"Appointments must fall within {_options.OpeningHour:00}:00 and {_options.ClosingHour:00}:00 and finish the same day.");
    }
}
