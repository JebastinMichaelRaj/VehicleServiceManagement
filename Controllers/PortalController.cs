using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Domain.Identity;
using VehicleServiceManagement.Services.Booking;
using VehicleServiceManagement.Services.Customers;
using VehicleServiceManagement.Services.Invoicing;
using Microsoft.AspNetCore.Identity;

namespace VehicleServiceManagement.Controllers;

/// <summary>Self-service portal: a customer sees only their own vehicles, services and invoices.</summary>
[Authorize(Roles = "Customer")]
public class PortalController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICustomerService _customers;
    private readonly IInvoiceService _invoices;
    private readonly IInvoicePdfGenerator _pdf;

    public PortalController(
        UserManager<ApplicationUser> users,
        ICustomerService customers,
        IInvoiceService invoices,
        IInvoicePdfGenerator pdf)
    {
        _users = users;
        _customers = customers;
        _invoices = invoices;
        _pdf = pdf;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var customerId = await CurrentCustomerIdAsync();
        if (customerId is null)
            return View("NotLinked");

        var details = await _customers.GetDetailsAsync(customerId.Value, cancellationToken);
        var invoices = await _invoices.ListAsync(null, customerId.Value, cancellationToken);

        ViewBag.Invoices = invoices;
        return View(details);
    }

    [HttpGet]
    public async Task<IActionResult> InvoicePdf(int id, CancellationToken cancellationToken)
    {
        var customerId = await CurrentCustomerIdAsync();
        var invoice = await _invoices.GetDetailsAsync(id, cancellationToken);

        // Ownership check: never reveal another customer's invoice, even by id guessing.
        if (customerId is null || invoice is null || invoice.CustomerId != customerId)
            return NotFound();

        return File(_pdf.Generate(invoice), "application/pdf", $"invoice-{id:D6}.pdf");
    }

    private async Task<int?> CurrentCustomerIdAsync()
    {
        var user = await _users.GetUserAsync(User);
        return user?.CustomerId;
    }
}
