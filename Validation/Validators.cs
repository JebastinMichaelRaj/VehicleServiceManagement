using FluentValidation;
using VehicleServiceManagement.Services.Booking;
using VehicleServiceManagement.Services.Customers;
using VehicleServiceManagement.Services.Inventory;

namespace VehicleServiceManagement.Validation;

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.VehicleId).GreaterThan(0).WithMessage("Please select a vehicle.");
        RuleFor(x => x.ServiceId).GreaterThan(0).WithMessage("Please select a service.");
        RuleFor(x => x.ScheduledStart).NotEqual(default(DateTime)).WithMessage("Please choose a date and time.");
        RuleFor(x => x.TechnicianName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CompleteBookingRequestValidator : AbstractValidator<CompleteBookingRequest>
{
    public CompleteBookingRequestValidator()
    {
        RuleFor(x => x.Mileage).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100).WithMessage("Discount must be between 0 and 100.");
        RuleForEach(x => x.Parts)
            .Must(p => p.Quantity >= 0 && (p.PartId > 0 || p.Quantity == 0))
            .WithMessage("Each part line needs a part and a quantity.");
    }
}

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.State).NotEmpty().MaximumLength(60).WithMessage("State is required (used for GST).");
    }
}

public sealed class VehicleRequestValidator : AbstractValidator<VehicleRequest>
{
    public VehicleRequestValidator()
    {
        RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Make).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Year).InclusiveBetween(1950, DateTime.Now.Year + 1);
        RuleFor(x => x.Mileage).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ServiceIntervalKm).InclusiveBetween(500, 100000);
        RuleFor(x => x.ServiceIntervalMonths).InclusiveBetween(1, 36);
    }
}

public sealed class PartRequestValidator : AbstractValidator<PartRequest>
{
    public PartRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.QuantityOnHand).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}
