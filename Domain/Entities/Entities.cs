namespace VehicleServiceManagement.Domain.Entities;

public enum BookingStatus { Scheduled, InProgress, Completed, Cancelled }

public enum InvoiceStatus { Unpaid, PartiallyPaid, Paid }

public enum PaymentMethod { Cash, Card, BankTransfer, Upi }

public class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    /// <summary>State of residence; used to decide intra-state (CGST+SGST) vs inter-state (IGST) tax.</summary>
    public string State { get; set; } = string.Empty;

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}

public class Vehicle
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Mileage { get; set; }
    /// <summary>Recommended service interval in kilometres.</summary>
    public int ServiceIntervalKm { get; set; } = 10000;
    /// <summary>Recommended service interval in months.</summary>
    public int ServiceIntervalMonths { get; set; } = 6;

    public Customer Customer { get; set; } = null!;
    public ICollection<ServiceBooking> Bookings { get; set; } = new List<ServiceBooking>();
    public ICollection<MaintenanceHistory> MaintenanceRecords { get; set; } = new List<MaintenanceHistory>();
}

public class ServiceCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
}

public class Service
{
    public int Id { get; set; }
    public int ServiceCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal EstimatedHours { get; set; }

    public ServiceCategory Category { get; set; } = null!;
}

/// <summary>Stock-keeping item used during jobs.</summary>
public class Part
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public int QuantityOnHand { get; set; }
    /// <summary>Raise a reorder alert when stock falls to or below this level.</summary>
    public int ReorderLevel { get; set; }
    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; set; } = [];

    public ICollection<JobPart> JobParts { get; set; } = new List<JobPart>();
}

public class ServiceBooking
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public int ServiceId { get; set; }
    public DateTime ScheduledStart { get; set; }
    /// <summary>Stored (not computed) so that overlap queries can run in SQL.</summary>
    public DateTime ScheduledEnd { get; set; }
    public decimal DurationHours { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Scheduled;
    public string TechnicianName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; set; } = [];

    public Vehicle Vehicle { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public Invoice? Invoice { get; set; }
    public ICollection<JobPart> Parts { get; set; } = new List<JobPart>();
}

/// <summary>A part consumed by a booking. Price is captured at the time of use.</summary>
public class JobPart
{
    public int Id { get; set; }
    public int ServiceBookingId { get; set; }
    public int PartId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }

    public ServiceBooking ServiceBooking { get; set; } = null!;
    public Part Part { get; set; } = null!;
}

public class Invoice
{
    public int Id { get; set; }
    public int ServiceBookingId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTime IssuedOn { get; set; }
    public DateTime DueDate { get; set; }
    /// <summary>When the last overdue reminder was sent; used to avoid spamming customers.</summary>
    public DateTime? LastReminderSentOn { get; set; }
    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; set; } = [];

    public ServiceBooking ServiceBooking { get; set; } = null!;
    public ICollection<PaymentRecord> Payments { get; set; } = new List<PaymentRecord>();
}

public class PaymentRecord
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidOn { get; set; }
    public PaymentMethod Method { get; set; }

    public Invoice Invoice { get; set; } = null!;
}

public class MaintenanceHistory
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public int ServiceBookingId { get; set; }
    public DateTime ServiceDate { get; set; }
    public int MileageAtService { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PartsUsed { get; set; } = string.Empty;
    public decimal LaborHours { get; set; }
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public string TechnicianName { get; set; } = string.Empty;

    public Vehicle Vehicle { get; set; } = null!;
    public ServiceBooking ServiceBooking { get; set; } = null!;
}
