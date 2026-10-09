using Microsoft.Extensions.Options;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Booking;
using Xunit;

namespace VehicleService.Tests;

public class BookingPolicyTests
{
    // Tuesday 6 Oct 2026, 09:00 - a fixed "now" keeps these tests deterministic.
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0);

    private static BookingPolicy CreatePolicy() =>
        new(Options.Create(new SchedulingOptions()));

    [Fact]
    public void Validate_AcceptsSlotWithinBusinessHours()
    {
        var policy = CreatePolicy();
        policy.Validate(new DateTime(2026, 10, 7, 10, 0, 0), 2m, Now);
    }

    [Fact]
    public void Validate_RejectsSlotInsideLeadTime()
    {
        var policy = CreatePolicy();
        Assert.Throws<BusinessRuleException>(() =>
            policy.Validate(new DateTime(2026, 10, 6, 9, 30, 0), 1m, Now));
    }

    [Fact]
    public void Validate_RejectsSundays()
    {
        var policy = CreatePolicy();
        Assert.Throws<BusinessRuleException>(() =>
            policy.Validate(new DateTime(2026, 10, 11, 10, 0, 0), 1m, Now));
    }

    [Fact]
    public void Validate_RejectsServiceThatOverrunsClosing()
    {
        var policy = CreatePolicy();
        // 17:00 start + 2h finishes at 19:00, after the 18:00 closing time.
        Assert.Throws<BusinessRuleException>(() =>
            policy.Validate(new DateTime(2026, 10, 7, 17, 0, 0), 2m, Now));
    }

    [Fact]
    public void Validate_RejectsZeroDuration()
    {
        var policy = CreatePolicy();
        Assert.Throws<BusinessRuleException>(() =>
            policy.Validate(new DateTime(2026, 10, 7, 10, 0, 0), 0m, Now));
    }
}
