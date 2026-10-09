using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Customers;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class CustomersController : Controller
{
    private readonly ICustomerService _customers;
    private readonly IValidator<CustomerRequest> _validator;

    public CustomersController(ICustomerService customers, IValidator<CustomerRequest> validator)
    {
        _customers = customers;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewBag.Query = q;
        return View(await _customers.SearchAsync(q, page, 15, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var details = await _customers.GetDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : View(details);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new CustomerRequest());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerRequest request, CancellationToken cancellationToken)
    {
        var id = await SaveAsync(request, isNew: true, cancellationToken);
        return id is null ? View("Form", request) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetForEditAsync(id, cancellationToken);
        return customer is null ? NotFound() : View("Form", customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerRequest request, CancellationToken cancellationToken)
    {
        request.Id = id;
        var saved = await SaveAsync(request, isNew: false, cancellationToken);
        return saved is null ? View("Form", request) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _customers.DeleteAsync(id, cancellationToken);
            TempData["Success"] = "Customer deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    /// <summary>Returns the new or updated customer id, or null when validation/business rules failed.</summary>
    private async Task<int?> SaveAsync(CustomerRequest request, bool isNew, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        foreach (var error in validation.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);

        if (!ModelState.IsValid) return null;

        try
        {
            if (isNew)
            {
                return await _customers.CreateAsync(request, cancellationToken);
            }

            await _customers.UpdateAsync(request, cancellationToken);
            return request.Id;
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return null;
        }
    }
}
