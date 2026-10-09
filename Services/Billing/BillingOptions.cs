namespace VehicleServiceManagement.Services.Billing;

/// <summary>Billing configuration bound from the "Billing" section of appsettings.json.</summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Total GST rate as a fraction, e.g. 0.18 for 18%.</summary>
    public decimal TaxRate { get; init; } = 0.18m;

    public int PaymentTermsDays { get; init; } = 15;

    /// <summary>The workshop's state. Customers in the same state get CGST+SGST; others get IGST.</summary>
    public string SellerState { get; init; } = "Tamil Nadu";
}
