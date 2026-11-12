using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Coupons;
using Clinic.Application.Features.Payments;
using Clinic.Infrastructure.Coupons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Infrastructure;

/// وحدة الدفع والكوبونات (الشخص 3). الشخص 1 بيضيف السطر ده في DependencyInjection.cs عند الدمج:
///     services.AddPaymentsModule(configuration);
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICouponRedeemer, CouponRedeemer>();
        services.AddScoped<CouponService>();
        services.AddScoped<ClinicPaymentService>();

        return services;
    }
}
