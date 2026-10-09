namespace VehicleServiceManagement.Services.Notifications;

/// <summary>Runs overdue-invoice reminders once a day in the background.</summary>
public sealed class OverdueReminderWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverdueReminderWorker> _logger;

    public OverdueReminderWorker(IServiceScopeFactory scopeFactory, ILogger<OverdueReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // Loop runs until the host shuts down; each pass uses a fresh DI scope so DbContext is never shared.
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IOverdueReminderService>();
                var sent = await reminders.SendDueRemindersAsync(stoppingToken);
                _logger.LogInformation("Overdue reminder pass complete: {Sent} sent", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Overdue reminder pass failed; will retry next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
