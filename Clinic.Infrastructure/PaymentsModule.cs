using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Coupons;
using Clinic.Application.Features.Payments;
using Clinic.Infrastructure.Coupons;
using Clinic.Infrastructure.Payments.Paymob;
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
        services.AddScoped<AdminCouponService>();
        services.AddScoped<ClinicPaymentService>();

        AddPaymob(services, configuration);
        services.AddScoped<OnlinePaymentService>();

        return services;
    }

    private static void AddPaymob(IServiceCollection services, IConfiguration configuration)
    {
        var settings = ReadPaymobSettings(configuration);

        if (settings is null)
        {
            // مفيش مفاتيح: التطبيق يشتغل عادي، والدفع الأونلاين بس هو اللي يرجّع خطأ واضح
            services.AddSingleton<IPaymobClient, UnconfiguredPaymobClient>();
            return;
        }

        services.AddSingleton(settings);
        services.AddSingleton<IPaymobClient>(_ => new PaymobClient(
            new HttpClient
            {
                BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            },
            settings));
    }

    private static PaymobSettings? ReadPaymobSettings(IConfiguration configuration)
    {
        var apiKey = configuration["Paymob:ApiKey"];
        var hmacSecret = configuration["Paymob:HmacSecret"];

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(hmacSecret)
            || !int.TryParse(configuration["Paymob:IntegrationId"], out var integrationId)
            || !int.TryParse(configuration["Paymob:IframeId"], out var iframeId))
        {
            return null;
        }

        var baseUrl = configuration["Paymob:BaseUrl"];
        var currency = configuration["Paymob:Currency"];

        return new PaymobSettings(
            apiKey,
            hmacSecret,
            integrationId,
            iframeId,
            string.IsNullOrWhiteSpace(baseUrl) ? "https://accept.paymob.com" : baseUrl,
            string.IsNullOrWhiteSpace(currency) ? "EGP" : currency);
    }
}
