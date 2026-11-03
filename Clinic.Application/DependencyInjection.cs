using Clinic.Application.Features.Auth;
using Clinic.Application.Features.Clinics;
using Clinic.Application.Features.Doctors;
using Clinic.Application.Features.Receptionists;
using Clinic.Application.Features.Schedules;
using Clinic.Application.Features.Specialties;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISpecialtyService, SpecialtyService>();
        services.AddScoped<IClinicService, ClinicService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IReceptionistService, ReceptionistService>();
        return services;
    }
}
