using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Services.Invoicing;

namespace VehicleServiceManagement.Controllers.Api;

/// <summary>REST endpoints for invoices and payments. Versioned via the URL: /api/v1/invoices.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/invoices")]
[Authorize(Roles = "Admin,Technician")]
public sealed class ApiInvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoices;

    public ApiInvoicesController(IInvoiceService invoices)
    {
        _invoices = invoices;
    }

    /// <summary>Lists invoices, optionally filtered by customer.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceListItemDto>>> List([FromQuery] int? customerId, CancellationToken cancellationToken)
        => Ok(await _invoices.ListAsync(null, customerId, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(InvoiceDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDetailsDto>> Get(int id, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetDetailsAsync(id, cancellationToken);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    /// <summary>Records a payment against an invoice.</summary>
    [HttpPost("{id:int}/payments")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] PaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _invoices.RecordPaymentAsync(id, request, cancellationToken);
            return NoContent();
        }
        catch (BusinessRuleException ex)
        {
            return Problem(title: "Payment rejected", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
