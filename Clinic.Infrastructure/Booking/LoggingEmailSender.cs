using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Booking;

/// تنفيذ مؤقت لـ IEmailSender: بيسجّل الإيميل في الـ log بس ومش بيبعته.
/// الشخص 1 هيستبدله بتنفيذ SMTP حقيقي في DependencyInjection.cs.
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("[Email not sent - SMTP not configured] To: {To} | Subject: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}