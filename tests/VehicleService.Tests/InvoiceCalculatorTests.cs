using VehicleServiceManagement.Services.Billing;
using Xunit;

namespace VehicleService.Tests;

public class InvoiceCalculatorTests
{
    [Fact]
    public void Calculate_AppliesDiscountBeforeTax()
    {
        var result = InvoiceCalculator.Calculate(subtotal: 1000m, discountPercent: 10m, taxRate: 0.18m);

        Assert.Equal(100m, result.DiscountAmount);
        Assert.Equal(900m, result.TaxableAmount);
        Assert.Equal(162m, result.TaxAmount);
        Assert.Equal(1062m, result.Total);
    }

    [Fact]
    public void Calculate_RoundsTaxToTwoDecimals()
    {
        var result = InvoiceCalculator.Calculate(subtotal: 99.99m, discountPercent: 0m, taxRate: 0.18m);

        Assert.Equal(18.00m, result.TaxAmount);
        Assert.Equal(117.99m, result.Total);
    }

    [Fact]
    public void Calculate_WithZeroDiscount_TotalEqualsSubtotalPlusTax()
    {
        var result = InvoiceCalculator.Calculate(subtotal: 240m, discountPercent: 0m, taxRate: 0.18m);

        Assert.Equal(0m, result.DiscountAmount);
        Assert.Equal(283.20m, result.Total);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Calculate_RejectsInvalidDiscount(decimal discount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InvoiceCalculator.Calculate(100m, discount, 0.18m));
    }

    [Fact]
    public void Calculate_RejectsNegativeSubtotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InvoiceCalculator.Calculate(-5m, 0m, 0.18m));
    }
}
