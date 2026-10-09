using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Dashboard;

public sealed record DashboardSummary(
    decimal RevenueCollected,
    decimal OutstandingBalance,
    int OverdueInvoices,
    int BookingsToday,
    int VehiclesInWorkshop,
    int TotalCustomers,
    int LowStockParts);

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DashboardService(IUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        var invoices = _unitOfWork.Repository<Invoice>().Query();
        var payments = _unitOfWork.Repository<PaymentRecord>().Query();
        var bookings = _unitOfWork.Repository<ServiceBooking>().Query();

        var totalBilled = await invoices.SumAsync(i => i.Total, cancellationToken);
        var collected = await payments.SumAsync(p => p.Amount, cancellationToken);
        var overdue = await invoices.CountAsync(i => i.Status != InvoiceStatus.Paid && i.DueDate < now, cancellationToken);
        var bookingsToday = await bookings.CountAsync(b =>
            b.ScheduledStart >= today && b.ScheduledStart < tomorrow && b.Status != BookingStatus.Cancelled, cancellationToken);
        var inWorkshop = await bookings.CountAsync(b => b.Status == BookingStatus.InProgress, cancellationToken);
        var customers = await _unitOfWork.Repository<Customer>().Query().CountAsync(cancellationToken);
        var lowStock = await _unitOfWork.Repository<Part>().Query()
            .CountAsync(p => p.QuantityOnHand <= p.ReorderLevel, cancellationToken);

        return new DashboardSummary(
            RevenueCollected: collected,
            OutstandingBalance: totalBilled - collected,
            OverdueInvoices: overdue,
            BookingsToday: bookingsToday,
            VehiclesInWorkshop: inWorkshop,
            TotalCustomers: customers,
            LowStockParts: lowStock);
    }
}
