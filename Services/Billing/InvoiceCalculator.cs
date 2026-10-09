namespace VehicleServiceManagement.Services.Billing;

/// <summary>Immutable result of an invoice calculation.</summary>
public sealed record InvoiceAmounts(decimal Subtotal, decimal DiscountAmount, decimal TaxableAmount, decimal TaxAmount, decimal Total);

/// <summary>
/// Pure, side-effect-free invoice arithmetic. Discount applies before tax; all amounts round to two
/// decimals using away-from-zero rounding.
/// </summary>
public static class InvoiceCalculator
{
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an input is outside its valid range.</exception>
    public static InvoiceAmounts Calculate(decimal subtotal, decimal discountPercent, decimal taxRate)
    {
        if (subtotal < 0) throw new ArgumentOutOfRangeException(nameof(subtotal), "Subtotal cannot be negative.");
        if (discountPercent < 0 || discountPercent > 100) throw new ArgumentOutOfRangeException(nameof(discountPercent), "Discount must be between 0 and 100.");
        if (taxRate < 0 || taxRate > 1) throw new ArgumentOutOfRangeException(nameof(taxRate), "Tax rate must be between 0 and 1.");

        var discountAmount = Round(subtotal * discountPercent / 100m);
        var taxableAmount = subtotal - discountAmount;
        var taxAmount = Round(taxableAmount * taxRate);
        var total = taxableAmount + taxAmount;

        return new InvoiceAmounts(subtotal, discountAmount, taxableAmount, taxAmount, total);
    }

    /// <summary>
    /// Splits GST into components. Intra-state: CGST and SGST are each half (SGST absorbs any rounding
    /// remainder so the parts always sum to the tax amount). Inter-state: all of it is IGST.
    /// </summary>
    public static (decimal Cgst, decimal Sgst, decimal Igst) SplitTax(decimal taxAmount, bool interState)
    {
        if (interState) return (0m, 0m, taxAmount);

        var cgst = Round(taxAmount / 2m);
        return (cgst, taxAmount - cgst, 0m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
