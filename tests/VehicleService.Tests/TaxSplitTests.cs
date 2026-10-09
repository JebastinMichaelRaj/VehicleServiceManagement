using VehicleServiceManagement.Services.Billing;
using Xunit;

namespace VehicleService.Tests;

public class TaxSplitTests
{
    [Fact]
    public void IntraState_SplitsEvenlyIntoCgstAndSgst()
    {
        var (cgst, sgst, igst) = InvoiceCalculator.SplitTax(36m, interState: false);

        Assert.Equal(18m, cgst);
        Assert.Equal(18m, sgst);
        Assert.Equal(0m, igst);
    }

    [Fact]
    public void IntraState_OddPaisaGoesToSgst_SoPartsSumToTotal()
    {
        var (cgst, sgst, igst) = InvoiceCalculator.SplitTax(0.05m, interState: false);

        Assert.Equal(cgst + sgst + igst, 0.05m);
    }

    [Fact]
    public void InterState_IsAllIgst()
    {
        var (cgst, sgst, igst) = InvoiceCalculator.SplitTax(46.80m, interState: true);

        Assert.Equal(0m, cgst);
        Assert.Equal(0m, sgst);
        Assert.Equal(46.80m, igst);
    }
}
