using AutoMapper;
using VehicleServiceManagement.Domain.Entities;
using VehicleServiceManagement.Services.Booking;

namespace VehicleServiceManagement.Mapping;

/// <summary>AutoMapper profile translating booking entities into read models (projected in SQL).</summary>
public sealed class BookingProfile : Profile
{
    public BookingProfile()
    {
        CreateMap<ServiceBooking, BookingListItemDto>()
            .ForMember(d => d.VehicleRegistration, o => o.MapFrom(s => s.Vehicle.RegistrationNumber))
            .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Vehicle.Customer.FullName))
            .ForMember(d => d.ServiceName, o => o.MapFrom(s => s.Service.Name));
    }
}
