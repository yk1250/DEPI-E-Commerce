using System.Threading;
using System.Threading.Tasks;

namespace Clinic.Application.Common.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}