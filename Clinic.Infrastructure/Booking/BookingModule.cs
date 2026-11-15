using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Appointments;
using Clinic.Application.Features.Slots;
using Clinic.Application.Features.Waitlist;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Infrastructure.Booking;

public static class BookingModule
{
    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        services.AddScoped<IDoctorAvailabilityReader, DoctorAvailabilityReader>();
        services.AddScoped<SlotService>();

        // TryAdd: لو الشخص 1 سجّل IEmailSender حقيقي قبل كده، ما نكتبش فوقه
        services.TryAddScoped<IEmailSender, LoggingEmailSender>();
        services.AddScoped<WaitlistService>();

        // AppointmentService بياخد WaitlistService تلقائياً من الـ DI
        services.AddScoped<AppointmentService>();
        services.AddScoped<AppointmentQueries>();

        // الـ Job بياخد IServiceScopeFactory بس، فـ Singleton آمن
        services.AddSingleton<ExpirePendingJob>();

        return services;
    }
}