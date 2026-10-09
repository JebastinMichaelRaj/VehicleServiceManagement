using VehicleServiceManagement.Domain.Entities;

namespace VehicleServiceManagement.Services.Invoicing;

public sealed record InvoiceListItemDto(
    int Id,
    int BookingId,
    int CustomerId,
    string VehicleRegistration,
    string CustomerName,
    decimal Total,
    decimal Balance,
    InvoiceStatus Status,
    DateTime DueDate,
    bool IsOverdue);

public sealed record InvoiceLineDto(string Description, int Quantity, decimal UnitPrice, decimal Amount);

public sealed record PaymentDto(DateTime PaidOn, decimal Amount, PaymentMethod Method);

public sealed record InvoiceDetailsDto(
    int Id,
    int BookingId,
    int CustomerId,
    string CustomerName,
    string VehicleRegistration,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxRate,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal Total,
    decimal AmountPaid,
    decimal Balance,
    InvoiceStatus Status,
    DateTime IssuedOn,
    DateTime DueDate,
    bool IsOverdue,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments);

/// <summary>Payment entry submitted from the UI or API.</summary>
public sealed class PaymentRequest
{
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
}
