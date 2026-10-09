using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Services.Invoicing;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class InvoicesController : Controller
{
    private readonly IInvoiceService _invoices;
    private readonly IInvoicePdfGenerator _pdf;

    public InvoicesController(IInvoiceService invoices, IInvoicePdfGenerator pdf)
    {
        _invoices = invoices;
        _pdf = pdf;
    }

    [HttpGet]
    public async Task<IActionResult> Index(InvoiceStatus? status, CancellationToken cancellationToken)
    {
        ViewBag.Status = status;
        return View(await _invoices.ListAsync(status, null, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetDetailsAsync(id, cancellationToken);
        return invoice is null ? NotFound() : View(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, PaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _invoices.RecordPaymentAsync(id, request, cancellationToken);
            TempData["Success"] = "Payment recorded.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Pdf(int id, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetDetailsAsync(id, cancellationToken);
        if (invoice is null) return NotFound();

        return File(_pdf.Generate(invoice), "application/pdf", $"invoice-{id:D6}.pdf");
    }
}
