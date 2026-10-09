using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Mapping;
using VehicleServiceManagement.Services.Billing;
using VehicleServiceManagement.Services.Booking;
using VehicleServiceManagement.Services.Inventory;
using VehicleServiceManagement.Services.Invoicing;
using VehicleServiceManagement.Services.Notifications;
using Xunit;

namespace VehicleService.IntegrationTests;

/// <summary>Runs the real EF Core + SQL Server stack: conflict detection, completion transaction, concurrency.</summary>
public sealed class BookingIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _sql;

    public BookingIntegrationTests(SqlServerFixture sql)
    {
        _sql = sql;
    }

    [Fact]
    public async Task OverlappingTechnicianBooking_IsRejected()
    {
        await ResetDatabaseAsync();
        await using var db = CreateContext();
        var booking = CreateBookingService(db);

        await booking.ScheduleAsync(new CreateBookingRequest
        {
            VehicleId = 1, ServiceId = 3, // Brake pads, 2h
            ScheduledStart = new DateTime(2030, 1, 7, 10, 0, 0),
            TechnicianName = "Ravi"
        });

        var conflict = () => booking.ScheduleAsync(new CreateBookingRequest
        {
            VehicleId = 2, ServiceId = 4, // Alignment, 1h, overlaps 10:00-12:00
            ScheduledStart = new DateTime(2030, 1, 7, 11, 0, 0),
            TechnicianName = "Ravi"
        });

        await Assert.ThrowsAsync<BusinessRuleException>(conflict);
    }

    [Fact]
    public async Task CompletingBooking_DeductsStock_RecordsHistory_AndIssuesInvoice()
    {
        await ResetDatabaseAsync();
        int bookingId;

        await using (var db = CreateContext())
        {
            bookingId = await CreateBookingService(db).ScheduleAsync(new CreateBookingRequest
            {
                VehicleId = 1, ServiceId = 1, // Oil & filter, base price 80
                ScheduledStart = new DateTime(2030, 1, 8, 9, 0, 0),
                TechnicianName = "Meena"
            });
        }

        await using (var db = CreateContext())
        {
            await CreateBookingService(db).CompleteAsync(bookingId, new CompleteBookingRequest
            {
                Mileage = 43000,
                DiscountPercent = 0,
                Parts = new List<PartUsageRequest> { new() { PartId = 2, Quantity = 1 } } // Oil filter, price 180
            });
        }

        await using var verify = CreateContext();

        var filterStock = await verify.Parts.Where(p => p.Id == 2).Select(p => p.QuantityOnHand).SingleAsync();
        Assert.Equal(14, filterStock);

        var vehicle = await verify.Vehicles.SingleAsync(v => v.Id == 1);
        Assert.Equal(43000, vehicle.Mileage);

        var history = await verify.MaintenanceHistories.SingleAsync(m => m.ServiceBookingId == bookingId);
        Assert.Equal(180m, history.PartsCost);

        var invoice = await verify.Invoices.Include(i => i.ServiceBooking).SingleAsync(i => i.ServiceBookingId == bookingId);
        // (80 labour + 180 part) = 260 subtotal; 18% GST = 46.80; total 306.80. Same state, so CGST+SGST.
        Assert.Equal(260m, invoice.Subtotal);
        Assert.Equal(46.80m, invoice.TaxAmount);
        Assert.Equal(306.80m, invoice.Total);
        Assert.Equal(23.40m, invoice.CgstAmount);
        Assert.Equal(23.40m, invoice.SgstAmount);
        Assert.Equal(0m, invoice.IgstAmount);
    }

    [Fact]
    public async Task CompletingWithInsufficientStock_RollsBackEverything()
    {
        await ResetDatabaseAsync();
        int bookingId;

        await using (var db = CreateContext())
        {
            bookingId = await CreateBookingService(db).ScheduleAsync(new CreateBookingRequest
            {
                VehicleId = 1, ServiceId = 1,
                ScheduledStart = new DateTime(2030, 1, 9, 9, 0, 0),
                TechnicianName = "Meena"
            });
        }

        await using (var db = CreateContext())
        {
            var complete = () => CreateBookingService(db).CompleteAsync(bookingId, new CompleteBookingRequest
            {
                Mileage = 43000,
                Parts = new List<PartUsageRequest> { new() { PartId = 4, Quantity = 10 } } // Air filter: only 3 in stock
            });

            await Assert.ThrowsAsync<BusinessRuleException>(complete);
        }

        await using var verify = CreateContext();
        var booking = await verify.ServiceBookings.SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Scheduled, booking.Status);
        Assert.Equal(3, await verify.Parts.Where(p => p.Id == 4).Select(p => p.QuantityOnHand).SingleAsync());
    }

    [Fact]
    public async Task StaleUpdate_IsRejectedByRowVersion()
    {
        await ResetDatabaseAsync();
        int bookingId;

        await using (var db = CreateContext())
        {
            bookingId = await CreateBookingService(db).ScheduleAsync(new CreateBookingRequest
            {
                VehicleId = 1, ServiceId = 1,
                ScheduledStart = new DateTime(2030, 1, 10, 9, 0, 0),
                TechnicianName = "Meena"
            });
        }

        // User A loads the booking and edits it, but has not saved yet.
        await using var userA = CreateContext();
        var staleCopy = await userA.ServiceBookings.SingleAsync(b => b.Id == bookingId);
        staleCopy.Notes = "Edited by A";

        // User B cancels the same booking and saves first.
        await using (var userB = CreateContext())
        {
            await CreateBookingService(userB).CancelAsync(bookingId);
        }

        // A's save must fail instead of silently overwriting B's change.
        var save = () => new UnitOfWork(userA).SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(save);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static BookingService CreateBookingService(AppDbContext db)
    {
        var uow = new UnitOfWork(db);
        var policy = new BookingPolicy(Options.Create(new SchedulingOptions()));
        var invoices = new InvoiceService(uow, Options.Create(new BillingOptions()), TimeProvider.System);
        var inventory = new InventoryService(uow, NullLogger<InventoryService>.Instance);
        var notifications = new LogNotificationSender(NullLogger<LogNotificationSender>.Instance);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<BookingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        return new BookingService(uow, policy, invoices, inventory, notifications, mapper,
            TimeProvider.System, NullLogger<BookingService>.Instance);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
