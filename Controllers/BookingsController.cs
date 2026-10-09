using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Booking;
using VehicleServiceManagement.Services.Inventory;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class BookingsController : Controller
{
    private readonly IBookingService _bookings;
    private readonly IInventoryService _inventory;
    private readonly IValidator<CreateBookingRequest> _createValidator;
    private readonly IValidator<CompleteBookingRequest> _completeValidator;
    private readonly TimeProvider _timeProvider;

    public BookingsController(
        IBookingService bookings,
        IInventoryService inventory,
        IValidator<CreateBookingRequest> createValidator,
        IValidator<CompleteBookingRequest> completeValidator,
        TimeProvider timeProvider)
    {
        _bookings = bookings;
        _inventory = inventory;
        _createValidator = createValidator;
        _completeValidator = completeValidator;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await _bookings.GetUpcomingAsync(_timeProvider.GetLocalNow().DateTime, 100, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await LoadLookupsAsync(cancellationToken);
        var defaultStart = _timeProvider.GetLocalNow().DateTime.Date.AddDays(1).AddHours(9);
        return View(new CreateBookingRequest { ScheduledStart = defaultStart });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        await AddValidationErrorsAsync(_createValidator, request, cancellationToken);

        if (ModelState.IsValid)
        {
            try
            {
                await _bookings.ScheduleAsync(request, cancellationToken);
                TempData["Success"] = "Appointment scheduled successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        await LoadLookupsAsync(cancellationToken);
        return View(request);
    }

    [HttpGet]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken)
    {
        await LoadPartsAsync(cancellationToken);
        ViewBag.BookingId = id;
        return View(new CompleteBookingRequest { Parts = Enumerable.Range(0, 5).Select(_ => new PartUsageRequest()).ToList() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, CompleteBookingRequest request, CancellationToken cancellationToken)
    {
        await AddValidationErrorsAsync(_completeValidator, request, cancellationToken);

        if (ModelState.IsValid)
        {
            try
            {
                await _bookings.CompleteAsync(id, request, cancellationToken);
                TempData["Success"] = "Service completed, stock updated and invoice issued.";
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        await LoadPartsAsync(cancellationToken);
        ViewBag.BookingId = id;
        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _bookings.CancelAsync(id, cancellationToken);
            TempData["Success"] = "Appointment cancelled.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        var lookups = await _bookings.GetLookupsAsync(cancellationToken);
        ViewBag.Vehicles = new SelectList(lookups.Vehicles, nameof(LookupItem.Id), nameof(LookupItem.Label));
        ViewBag.Services = new SelectList(lookups.Services, nameof(LookupItem.Id), nameof(LookupItem.Label));
    }

    private async Task LoadPartsAsync(CancellationToken cancellationToken)
    {
        var parts = await _inventory.ListAsync(null, false, cancellationToken);
        ViewBag.Parts = new SelectList(parts, nameof(PartListItemDto.Id), nameof(PartListItemDto.Name));
    }

    /// <summary>Runs a FluentValidation validator and copies its errors into ModelState.</summary>
    internal async Task AddValidationErrorsAsync<T>(IValidator<T> validator, T model, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(model, cancellationToken);
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
    }
}
