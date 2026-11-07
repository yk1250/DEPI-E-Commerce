namespace Clinic.Application.Common.Interfaces;

/// القاعدة الوحيدة اللي بتحدد مين يشوف البيانات الطبية لمريض (السجل الطبي والمرفقات):
///   - المريض: بياناته هو بس.
///   - الدكتور: مريض عنده معاه موعد (Confirmed أو CheckedIn أو Completed) بس.
///   - الأدمن والريسبشن: ممنوعين دايماً.
/// أي Service بيرجّع بيانات طبية لازم يعدّي من هنا الأول.
public interface IPatientAccessGuard
{
    /// true لو المستخدم الحالي مسموح له يشوف بيانات المريض ده.
    Task<bool> CanAccessAsync(int patientId, CancellationToken ct = default);

    /// يرمي UnauthorizedException (401) لو مفيش تسجيل دخول، وForbiddenException (403) لو غير مسموح.
    Task EnsureAccessAsync(int patientId, CancellationToken ct = default);
}
