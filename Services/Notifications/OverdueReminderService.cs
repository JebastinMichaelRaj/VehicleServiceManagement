using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Notifications;

public interface IOverdueReminderService
{
    /// <summary>Sends reminders for overdue invoices not reminded in the last 7 days. Returns how many were sent.</summary>
    Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default);
}

public sealed class OverdueReminderService : IOverdueReminderService
{
    private static readonly TimeSpan ReminderCooldown = TimeSpan.FromDays(7);

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationSender _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OverdueReminderService> _logger;

    public OverdueReminderService(
        IUnitOfWork unitOfWork,
        INotificationSender notifications,
        TimeProvider timeProvider,
        ILogger<OverdueReminderService> logger)
    {
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var cooldownCutoff = now - ReminderCooldown;

        var overdue = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.ServiceBooking).ThenInclude(b => b.Vehicle).ThenInclude(v => v.Customer)
            .Where(i => i.Status != InvoiceStatus.Paid
                && i.DueDate < now
                && (i.LastReminderSentOn == null || i.LastReminderSentOn < cooldownCutoff))
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var invoice in overdue)
        {
            var customer = invoice.ServiceBooking.Vehicle.Customer;
            try
            {
                await _notifications.SendAsync(new NotificationMessage(
                    customer.Email,
                    customer.Phone,
                    "Payment reminder",
                    $"Invoice #{invoice.Id} was due on {invoice.DueDate:dd MMM yyyy}. Please settle the outstanding balance."),
                    cancellationToken);

                invoice.LastReminderSentOn = now;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failing recipient must not stop reminders for everyone else.
                _logger.LogWarning(ex, "Overdue reminder failed for invoice {InvoiceId}", invoice.Id);
            }
        }

        if (sent > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return sent;
    }
}
