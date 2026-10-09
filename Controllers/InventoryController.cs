using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Inventory;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class InventoryController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IValidator<PartRequest> _validator;

    public InventoryController(IInventoryService inventory, IValidator<PartRequest> validator)
    {
        _inventory = inventory;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, bool lowStock = false, CancellationToken cancellationToken = default)
    {
        ViewBag.Query = q;
        ViewBag.LowStock = lowStock;
        return View(await _inventory.ListAsync(q, lowStock, cancellationToken));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Create() => View("Form", new PartRequest());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PartRequest request, CancellationToken cancellationToken)
    {
        if (!await ValidateAsync(request, cancellationToken)) return View("Form", request);

        try
        {
            await _inventory.CreateAsync(request, cancellationToken);
            TempData["Success"] = "Part added to the catalogue.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Form", request);
        }
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var part = await _inventory.GetForEditAsync(id, cancellationToken);
        return part is null ? NotFound() : View("Form", part);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PartRequest request, CancellationToken cancellationToken)
    {
        request.Id = id;
        if (!await ValidateAsync(request, cancellationToken)) return View("Form", request);

        try
        {
            await _inventory.UpdateAsync(request, cancellationToken);
            TempData["Success"] = "Part updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            // Stale edits (someone else changed the part) and duplicate SKUs both land here.
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Form", request);
        }
    }

    private async Task<bool> ValidateAsync(PartRequest request, CancellationToken cancellationToken)
    {
        var result = await _validator.ValidateAsync(request, cancellationToken);
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        return ModelState.IsValid;
    }
}
