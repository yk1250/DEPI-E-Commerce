using Clinic.Application.Features.Appointments;
using Clinic.Application.Features.Slots;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Infrastructure.Booking;

public static class BookingModule
{
    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        services.AddScoped<IDoctorAvailabilityReader, DoctorAvailabilityReader>();
        services.AddScoped<SlotService>();
        services.AddScoped<AppointmentService>();

        // الـ Job بياخد IServiceScopeFactory بس، فـ Singleton آمن
        services.AddSingleton<ExpirePendingJob>();

        return services;
    }
}