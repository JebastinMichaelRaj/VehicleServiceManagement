using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VehicleServiceManagement.Common.Exceptions;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Services.Billing;

namespace VehicleServiceManagement.Services.Invoicing;

public interface IInvoiceService
{
    /// <summary>
    /// Creates an invoice for a completed booking and tracks it in the unit of work. Does NOT save;
    /// the caller commits so the booking, history and invoice are saved together.
    /// </summary>
    Task<Invoice> CreateForCompletedBookingAsync(ServiceBooking booking, decimal partsCost, decimal discountPercent, string? customerState, CancellationToken cancellationToken = default);

    Task RecordPaymentAsync(int invoiceId, PaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists invoices, optionally filtered by status and/or customer.</summary>
    Task<IReadOnlyList<InvoiceListItemDto>> ListAsync(InvoiceStatus? status, int? customerId, CancellationToken cancellationToken = default);

    Task<InvoiceDetailsDto?> GetDetailsAsync(int invoiceId, CancellationToken cancellationToken = default);
}

public sealed class InvoiceService : IInvoiceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly BillingOptions _billing;
    private readonly TimeProvider _timeProvider;

    public InvoiceService(IUnitOfWork unitOfWork, IOptions<BillingOptions> billing, TimeProvider timeProvider)
    {
        _unitOfWork = unitOfWork;
        _billing = billing.Value;
        _timeProvider = timeProvider;
    }

    public async Task<Invoice> CreateForCompletedBookingAsync(ServiceBooking booking, decimal partsCost, decimal discountPercent, string? customerState, CancellationToken cancellationToken = default)
    {
        if (booking.Status != BookingStatus.Completed)
            throw new BusinessRuleException("Invoices can only be generated for completed bookings.");

        if (partsCost < 0)
            throw new BusinessRuleException("Parts cost cannot be negative.");

        var alreadyInvoiced = await _unitOfWork.Repository<Invoice>().Query()
            .AnyAsync(i => i.ServiceBookingId == booking.Id, cancellationToken);
        if (alreadyInvoiced)
            throw new BusinessRuleException($"Booking {booking.Id} already has an invoice.");

        var subtotal = booking.Service.BasePrice + partsCost;
        var amounts = InvoiceCalculator.Calculate(subtotal, discountPercent, _billing.TaxRate);

        // Inter-state supply when the customer's state is known and differs from the workshop's state.
        var interState = !string.IsNullOrWhiteSpace(customerState)
            && !string.Equals(customerState.Trim(), _billing.SellerState.Trim(), StringComparison.OrdinalIgnoreCase);
        var (cgst, sgst, igst) = InvoiceCalculator.SplitTax(amounts.TaxAmount, interState);

        var now = _timeProvider.GetLocalNow().DateTime;
        var invoice = new Invoice
        {
            ServiceBookingId = booking.Id,
            Subtotal = amounts.Subtotal,
            DiscountPercent = discountPercent,
            DiscountAmount = amounts.DiscountAmount,
            TaxRate = _billing.TaxRate,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            TaxAmount = amounts.TaxAmount,
            Total = amounts.Total,
            Status = InvoiceStatus.Unpaid,
            IssuedOn = now,
            DueDate = now.AddDays(_billing.PaymentTermsDays)
        };

        await _unitOfWork.Repository<Invoice>().AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task RecordPaymentAsync(int invoiceId, PaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new BusinessRuleException("Payment amount must be greater than zero.");

        var invoice = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new BusinessRuleException($"Invoice {invoiceId} was not found.");

        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessRuleException("This invoice is already fully paid.");

        var balance = invoice.Total - invoice.Payments.Sum(p => p.Amount);
        if (request.Amount > balance)
            throw new BusinessRuleException($"Payment ({request.Amount:N2}) exceeds the outstanding balance ({balance:N2}).");

        await _unitOfWork.Repository<PaymentRecord>().AddAsync(new PaymentRecord
        {
            InvoiceId = invoice.Id,
            Amount = request.Amount,
            PaidOn = _timeProvider.GetLocalNow().DateTime,
            Method = request.Method
        }, cancellationToken);

        invoice.Status = request.Amount == balance ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceListItemDto>> ListAsync(InvoiceStatus? status, int? customerId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var query = _unitOfWork.Repository<Invoice>().Query();

        if (status is not null)
            query = query.Where(i => i.Status == status.Value);

        if (customerId is not null)
            query = query.Where(i => i.ServiceBooking.Vehicle.CustomerId == customerId.Value);

        var rows = await query
            .OrderByDescending(i => i.IssuedOn)
            .Take(200)
            .Select(i => new
            {
                i.Id,
                i.ServiceBookingId,
                CustomerId = i.ServiceBooking.Vehicle.CustomerId,
                Registration = i.ServiceBooking.Vehicle.RegistrationNumber,
                Customer = i.ServiceBooking.Vehicle.Customer.FullName,
                i.Total,
                Paid = i.Payments.Sum(p => p.Amount),
                i.Status,
                i.DueDate
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new InvoiceListItemDto(
            r.Id, r.ServiceBookingId, r.CustomerId, r.Registration, r.Customer, r.Total,
            r.Total - r.Paid, r.Status, r.DueDate,
            r.Status != InvoiceStatus.Paid && r.DueDate < now)).ToList();
    }

    public async Task<InvoiceDetailsDto?> GetDetailsAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;

        var invoice = await _unitOfWork.Repository<Invoice>().Query()
            .Include(i => i.Payments)
            .Include(i => i.ServiceBooking).ThenInclude(b => b.Service)
            .Include(i => i.ServiceBooking).ThenInclude(b => b.Vehicle).ThenInclude(v => v.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice is null) return null;

        var parts = await _unitOfWork.Repository<JobPart>().Query()
            .Where(j => j.ServiceBookingId == invoice.ServiceBookingId)
            .Select(j => new { j.Part.Name, j.Quantity, j.UnitPrice })
            .ToListAsync(cancellationToken);

        var lines = new List<InvoiceLineDto>
        {
            new(invoice.ServiceBooking.Service.Name, 1, invoice.ServiceBooking.Service.BasePrice, invoice.ServiceBooking.Service.BasePrice)
        };
        lines.AddRange(parts.Select(p => new InvoiceLineDto(p.Name, p.Quantity, p.UnitPrice, p.Quantity * p.UnitPrice)));

        var paid = invoice.Payments.Sum(p => p.Amount);
        var balance = invoice.Total - paid;
        var customer = invoice.ServiceBooking.Vehicle.Customer;

        return new InvoiceDetailsDto(
            invoice.Id,
            invoice.ServiceBookingId,
            customer.Id,
            customer.FullName,
            invoice.ServiceBooking.Vehicle.RegistrationNumber,
            invoice.Subtotal,
            invoice.DiscountPercent,
            invoice.DiscountAmount,
            invoice.TaxRate,
            invoice.CgstAmount,
            invoice.SgstAmount,
            invoice.IgstAmount,
            invoice.Total,
            paid,
            balance,
            invoice.Status,
            invoice.IssuedOn,
            invoice.DueDate,
            invoice.Status != InvoiceStatus.Paid && invoice.DueDate < now,
            lines,
            invoice.Payments.OrderBy(p => p.PaidOn)
                .Select(p => new PaymentDto(p.PaidOn, p.Amount, p.Method)).ToList());
    }
}
