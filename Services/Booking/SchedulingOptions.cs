namespace VehicleServiceManagement.Services.Booking;

/// <summary>Workshop scheduling rules bound from the "Scheduling" section of appsettings.json.</summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>Hour of day (24h) when the workshop opens.</summary>
    public int OpeningHour { get; init; } = 8;

    /// <summary>Hour of day (24h) when the workshop closes. Services must finish before this.</summary>
    public int ClosingHour { get; init; } = 18;

    /// <summary>Minimum time between now and a booking start.</summary>
    public double MinimumLeadTimeHours { get; init; } = 1;
}
