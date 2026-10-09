using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Domain.Identity;

namespace VehicleServiceManagement.Data;

/// <summary>EF Core context: domain tables plus ASP.NET Core Identity tables.</summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<ServiceBooking> ServiceBookings => Set<ServiceBooking>();
    public DbSet<JobPart> JobParts => Set<JobPart>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<MaintenanceHistory> MaintenanceHistories => Set<MaintenanceHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Required for Identity tables.

        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(c => c.FullName).HasMaxLength(150).IsRequired();
            e.Property(c => c.Email).HasMaxLength(256).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            e.Property(c => c.State).HasMaxLength(60).IsRequired();
            e.HasIndex(c => c.Email).IsUnique();
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            e.Property(v => v.RegistrationNumber).HasMaxLength(20).IsRequired();
            e.Property(v => v.Make).HasMaxLength(60).IsRequired();
            e.Property(v => v.Model).HasMaxLength(60).IsRequired();
            e.HasIndex(v => v.RegistrationNumber).IsUnique();
            e.HasOne(v => v.Customer)
                .WithMany(c => c.Vehicles)
                .HasForeignKey(v => v.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServiceCategory>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.Property(c => c.Description).HasMaxLength(500);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Service>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(150).IsRequired();
            e.Property(s => s.BasePrice).HasPrecision(18, 2);
            e.Property(s => s.EstimatedHours).HasPrecision(5, 2);
            e.HasOne(s => s.Category)
                .WithMany(c => c.Services)
                .HasForeignKey(s => s.ServiceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Part>(e =>
        {
            e.Property(p => p.Sku).HasMaxLength(40).IsRequired();
            e.Property(p => p.Name).HasMaxLength(150).IsRequired();
            e.Property(p => p.UnitCost).HasPrecision(18, 2);
            e.Property(p => p.UnitPrice).HasPrecision(18, 2);
            e.Property(p => p.RowVersion).IsRowVersion();
            e.HasIndex(p => p.Sku).IsUnique();
        });

        modelBuilder.Entity<ServiceBooking>(e =>
        {
            e.Property(b => b.DurationHours).HasPrecision(5, 2);
            e.Property(b => b.TechnicianName).HasMaxLength(100).IsRequired();
            e.Property(b => b.Notes).HasMaxLength(500);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.RowVersion).IsRowVersion();
            e.HasIndex(b => new { b.TechnicianName, b.ScheduledStart });
            e.HasIndex(b => new { b.VehicleId, b.ScheduledStart });
            e.HasOne(b => b.Vehicle)
                .WithMany(v => v.Bookings)
                .HasForeignKey(b => b.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Service)
                .WithMany()
                .HasForeignKey(b => b.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JobPart>(e =>
        {
            e.Property(j => j.UnitPrice).HasPrecision(18, 2);
            e.Property(j => j.UnitCost).HasPrecision(18, 2);
            e.HasOne(j => j.ServiceBooking)
                .WithMany(b => b.Parts)
                .HasForeignKey(j => j.ServiceBookingId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.Part)
                .WithMany(p => p.JobParts)
                .HasForeignKey(j => j.PartId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.Property(i => i.Subtotal).HasPrecision(18, 2);
            e.Property(i => i.DiscountPercent).HasPrecision(5, 2);
            e.Property(i => i.DiscountAmount).HasPrecision(18, 2);
            e.Property(i => i.TaxRate).HasPrecision(5, 4);
            e.Property(i => i.CgstAmount).HasPrecision(18, 2);
            e.Property(i => i.SgstAmount).HasPrecision(18, 2);
            e.Property(i => i.IgstAmount).HasPrecision(18, 2);
            e.Property(i => i.TaxAmount).HasPrecision(18, 2);
            e.Property(i => i.Total).HasPrecision(18, 2);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.RowVersion).IsRowVersion();
            e.HasIndex(i => i.ServiceBookingId).IsUnique();
            e.HasIndex(i => i.Status);
            e.HasOne(i => i.ServiceBooking)
                .WithOne(b => b.Invoice)
                .HasForeignKey<Invoice>(i => i.ServiceBookingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentRecord>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
            e.HasOne(p => p.Invoice)
                .WithMany(i => i.Payments)
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MaintenanceHistory>(e =>
        {
            e.Property(m => m.Description).HasMaxLength(200).IsRequired();
            e.Property(m => m.PartsUsed).HasMaxLength(1000);
            e.Property(m => m.TechnicianName).HasMaxLength(100).IsRequired();
            e.Property(m => m.LaborHours).HasPrecision(5, 2);
            e.Property(m => m.LaborCost).HasPrecision(18, 2);
            e.Property(m => m.PartsCost).HasPrecision(18, 2);
            e.HasIndex(m => new { m.VehicleId, m.ServiceDate });
            e.HasOne(m => m.Vehicle)
                .WithMany(v => v.MaintenanceRecords)
                .HasForeignKey(m => m.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.ServiceBooking)
                .WithMany()
                .HasForeignKey(m => m.ServiceBookingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        SeedReferenceData(modelBuilder);
    }

    /// <summary>Static seed data so migrations are deterministic (fixed keys, no DateTime.Now).</summary>
    private static void SeedReferenceData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceCategory>().HasData(
            new ServiceCategory { Id = 1, Name = "Routine Maintenance", Description = "Oil, filters, fluids and inspections." },
            new ServiceCategory { Id = 2, Name = "Brakes & Tyres", Description = "Brake pads, discs, alignment and tyres." },
            new ServiceCategory { Id = 3, Name = "Engine & Diagnostics", Description = "Engine repairs and computer diagnostics." });

        modelBuilder.Entity<Service>().HasData(
            new Service { Id = 1, ServiceCategoryId = 1, Name = "Oil & Filter Change", BasePrice = 80m, EstimatedHours = 0.75m },
            new Service { Id = 2, ServiceCategoryId = 1, Name = "Full Inspection", BasePrice = 120m, EstimatedHours = 1.5m },
            new Service { Id = 3, ServiceCategoryId = 2, Name = "Brake Pad Replacement", BasePrice = 240m, EstimatedHours = 2m },
            new Service { Id = 4, ServiceCategoryId = 2, Name = "Wheel Alignment", BasePrice = 90m, EstimatedHours = 1m },
            new Service { Id = 5, ServiceCategoryId = 3, Name = "Computer Diagnostics", BasePrice = 150m, EstimatedHours = 1m });

        modelBuilder.Entity<Part>().HasData(
            new Part { Id = 1, Sku = "OIL-5W30-1L", Name = "Engine oil 5W-30 (1 L)", UnitCost = 350m, UnitPrice = 450m, QuantityOnHand = 40, ReorderLevel = 10 },
            new Part { Id = 2, Sku = "FLT-OIL-01", Name = "Oil filter", UnitCost = 120m, UnitPrice = 180m, QuantityOnHand = 15, ReorderLevel = 5 },
            new Part { Id = 3, Sku = "BRK-PAD-SET", Name = "Brake pad set (front)", UnitCost = 900m, UnitPrice = 1200m, QuantityOnHand = 6, ReorderLevel = 4 },
            new Part { Id = 4, Sku = "FLT-AIR-01", Name = "Air filter", UnitCost = 200m, UnitPrice = 320m, QuantityOnHand = 3, ReorderLevel = 5 });

        modelBuilder.Entity<Customer>().HasData(
            new Customer { Id = 1, FullName = "Arjun Mehta", Email = "arjun.mehta@example.com", Phone = "+91-98400-11122", State = "Tamil Nadu" },
            new Customer { Id = 2, FullName = "Priya Raman", Email = "priya.raman@example.com", Phone = "+91-98410-33344", State = "Karnataka" });

        modelBuilder.Entity<Vehicle>().HasData(
            new Vehicle { Id = 1, CustomerId = 1, RegistrationNumber = "TN-58-AB-1234", Make = "Toyota", Model = "Corolla", Year = 2019, Mileage = 42000 },
            new Vehicle { Id = 2, CustomerId = 2, RegistrationNumber = "KA-01-CD-5678", Make = "Honda", Model = "City", Year = 2021, Mileage = 18500 });
    }
}
