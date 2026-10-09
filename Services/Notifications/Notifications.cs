using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace VehicleServiceManagement.Services.Notifications;

/// <summary>Message to a customer. Channels pick the address they need (email or phone).</summary>
public sealed record NotificationMessage(string? Email, string? Phone, string Subject, string Body);

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Writes notifications to the log. Default channel for development and testing.</summary>
public sealed class LogNotificationSender : INotificationSender
{
    private readonly ILogger<LogNotificationSender> _logger;

    public LogNotificationSender(ILogger<LogNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Notification to {Email}/{Phone}: {Subject} - {Body}",
            message.Email, message.Phone, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string SenderId { get; init; } = "VSM";
}

/// <summary>
/// Sends SMS through a generic JSON gateway. The HttpClient is configured with a standard resilience
/// pipeline (retries with backoff, circuit breaker, timeouts) in Program.cs.
/// </summary>
public sealed class SmsNotificationSender : INotificationSender
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<SmsNotificationSender> _logger;

    public SmsNotificationSender(HttpClient http, IOptions<SmsOptions> options, ILogger<SmsNotificationSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.Phone))
        {
            _logger.LogWarning("Skipped SMS '{Subject}': customer has no phone number.", message.Subject);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = JsonContent.Create(new
            {
                to = message.Phone,
                from = _options.SenderId,
                text = $"{message.Subject}: {message.Body}"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
