namespace Clinic.Application.Common.Interfaces;

/// تأكيد موعد بعد نجاح الدفع الأونلاين.
/// PendingPayment يبقى Confirmed ويتمسح ExpiresAt. الموعد المؤكد بالفعل: ما يحصلش حاجة.
/// الموعد Expired أو Cancelled أو NoShow: ConflictException.
/// (لو الشخص 2 رفع نسخته من ConfirmAsync في AppointmentService، نبدّل التسجيل في PaymentsModule بس.)
public interface IAppointmentConfirmer
{
    Task ConfirmAsync(int appointmentId, CancellationToken ct = default);
}
