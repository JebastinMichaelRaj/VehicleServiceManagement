using Microsoft.AspNetCore.Identity;

namespace VehicleServiceManagement.Domain.Identity;

/// <summary>
/// Application user. Customer-role users are linked to a <see cref="Entities.Customer"/> so the
/// self-service portal can show only their own bookings and invoices.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public int? CustomerId { get; set; }
}
