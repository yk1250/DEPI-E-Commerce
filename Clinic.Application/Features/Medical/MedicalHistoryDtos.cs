using System.ComponentModel.DataAnnotations;

namespace Clinic.Application.Features.Medical;

/// السجل الطبي بعد فك التشفير. لو المريض لسه ما كتبش حاجة: كل الحقول null و UpdatedAt = null.
public sealed record MedicalHistoryDto(
    int PatientId,
    string? ChronicConditions,
    string? Allergies,
    string? CurrentMedications,
    DateTime? UpdatedAt);

/// PUT بيستبدل السجل كله: أي حقل null أو فاضي بيتمسح.
public class UpdateMedicalHistoryRequest
{
    /// الأمراض المزمنة (حد أقصى 2000 حرف).
    [StringLength(2000)]
    public string? ChronicConditions { get; init; }

    /// الحساسية (حد أقصى 2000 حرف).
    [StringLength(2000)]
    public string? Allergies { get; init; }

    /// الأدوية الحالية (حد أقصى 2000 حرف).
    [StringLength(2000)]
    public string? CurrentMedications { get; init; }
}
