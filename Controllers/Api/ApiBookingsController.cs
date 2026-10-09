using FluentValidation;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Booking;

namespace VehicleServiceManagement.Controllers.Api;

/// <summary>REST endpoints for bookings. Versioned via the URL segment: /api/v1/bookings.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/bookings")]
[Authorize(Roles = "Admin,Technician")]
public sealed class ApiBookingsController : ControllerBase
{
    private readonly IBookingService _bookings;
    private readonly IValidator<CreateBookingRequest> _createValidator;
    private readonly TimeProvider _timeProvider;

    public ApiBookingsController(IBookingService bookings, IValidator<CreateBookingRequest> createValidator, TimeProvider timeProvider)
    {
        _bookings = bookings;
        _createValidator = createValidator;
        _timeProvider = timeProvider;
    }

    /// <summary>Lists upcoming, non-cancelled bookings.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BookingListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BookingListItemDto>>> GetUpcoming([FromQuery] int take = 50, CancellationToken cancellationToken = default)
        => Ok(await _bookings.GetUpcomingAsync(_timeProvider.GetLocalNow().DateTime, Math.Clamp(take, 1, 200), cancellationToken));

    /// <summary>Schedules an appointment.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Schedule([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        try
        {
            var id = await _bookings.ScheduleAsync(request, cancellationToken);
            return Created($"/api/v1/bookings/{id}", new { id });
        }
        catch (BusinessRuleException ex)
        {
            return Problem(title: "Booking rule violated", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Cancels a booking that has not been completed.</summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _bookings.CancelAsync(id, cancellationToken);
            return NoContent();
        }
        catch (BusinessRuleException ex)
        {
            return Problem(title: "Cancellation not allowed", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Completes a job, deducts stock and issues the invoice.</summary>
    [HttpPost("{id:int}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(int id, [FromBody] CompleteBookingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _bookings.CompleteAsync(id, request, cancellationToken);
            return NoContent();
        }
        catch (BusinessRuleException ex)
        {
            return Problem(title: "Completion not allowed", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
