using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Customers;
using VehicleServiceManagement.Services.Maintenance;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class VehiclesController : Controller
{
    private readonly ICustomerService _customers;
    private readonly IMaintenanceService _maintenance;
    private readonly IValidator<VehicleRequest> _validator;

    public VehiclesController(ICustomerService customers, IMaintenanceService maintenance, IValidator<VehicleRequest> validator)
    {
        _customers = customers;
        _maintenance = maintenance;
        _validator = validator;
    }

    [HttpGet]
    public IActionResult Create(int customerId) => View("Form", new VehicleRequest { CustomerId = customerId });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VehicleRequest request, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(request, cancellationToken))
        {
            try
            {
                await _customers.AddVehicleAsync(request, cancellationToken);
                TempData["Success"] = "Vehicle added.";
                return RedirectToAction("Details", "Customers", new { id = request.CustomerId });
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", request);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var vehicle = await _customers.GetVehicleForEditAsync(id, cancellationToken);
        return vehicle is null ? NotFound() : View("Form", vehicle);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, VehicleRequest request, CancellationToken cancellationToken)
    {
        request.Id = id;
        if (await ValidateAsync(request, cancellationToken))
        {
            try
            {
                await _customers.UpdateVehicleAsync(request, cancellationToken);
                TempData["Success"] = "Vehicle updated.";
                return RedirectToAction("Details", "Customers", new { id = request.CustomerId });
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", request);
    }

    [HttpGet]
    public async Task<IActionResult> History(int id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _maintenance.GetHistoryAsync(id, cancellationToken));
        }
        catch (BusinessRuleException)
        {
            return NotFound();
        }
    }

    private async Task<bool> ValidateAsync(VehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await _validator.ValidateAsync(request, cancellationToken);
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        return ModelState.IsValid;
    }
}
